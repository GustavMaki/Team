using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Tharga.Team.Email;

/// <summary>
/// Sends over SMTP and reads over IMAP, using MailKit.
/// </summary>
/// <remarks>
/// <b>Never throws</b>, matching <c>SlackClient</c>. A failed send comes back as a failed
/// <see cref="MailSendResult"/> and a failed read as an empty fetch at the unchanged position carrying
/// <see cref="MailFetchResult.Error"/>, so a mail
/// server being down delays a conversation instead of breaking the application that hosts it.
/// <para>
/// <b>A connection per operation.</b> Both clients are opened, used and disposed rather than pooled. A
/// long-lived IMAP connection is the shape this design rejected when it chose polling over a socket: it is
/// process-local state that has to be revived after every network blip, and it buys nothing at a poll
/// interval measured in minutes.
/// </para>
/// <para>
/// <b>Every mail read has its sender checked</b> by <see cref="DkimSenderVerifier"/>, the process-wide one over
/// DNS unless another is given, so <see cref="InboundMail.SenderVerified"/> means the same on every read path.
/// </para>
/// </remarks>
internal sealed class MailClient(IOptions<MailOptions> options, ILogger<MailClient> logger = null, DkimSenderVerifier senderVerifier = null, MailReadHealth health = null) : IMailClient
{
    private readonly MailReadHealth _health = health ?? MailReadHealth.Shared;
    /// <summary>How many messages one poll will take, so a large backlog cannot stall a deployment.</summary>
    private const int MaxMessagesPerFetch = 100;

    private readonly MailOptions _options = options?.Value ?? new MailOptions();

    public bool CanSend => _options.Smtp.IsConfigured && !string.IsNullOrWhiteSpace(_options.FromAddress);

    public bool CanRead => _options.Imap.IsConfigured;

    public string DefaultFrom => string.IsNullOrWhiteSpace(_options.FromAddress) ? null : _options.FromAddress.Trim();

    public async Task<MailSendResult> SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        if (!CanSend) return MailSendResult.Failed("Email is not configured for sending.");
        if (string.IsNullOrWhiteSpace(mail.To)) return MailSendResult.Failed("No recipient.");

        try
        {
            var message = MailMessageFactory.Create(mail, _options);

            using var client = new SmtpClient { Timeout = (int)_options.Timeout.TotalMilliseconds };

            await ConnectAsync(client, _options.Smtp, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return MailSendResult.Ok(message.MessageId);
        }
        catch (Exception e)
        {
            // The recipient is deliberately not logged: an address is personal data, and logs travel further than mail.
            logger?.LogError(e, "A mail could not be sent.");

            return e is SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted }
                ? MailSendResult.SenderNotAccepted(e.Message)
                : MailSendResult.Failed(e.Message);
        }
    }

    public Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken = default) => FetchAsync(position, false, cancellationToken);

    /// <summary>Fetches as <see cref="FetchAsync(MailFetchPosition, CancellationToken)"/> does, and with <paramref name="includeRaw"/> keeps each message as it arrived.</summary>
    public async Task<MailFetchResult> FetchAsync(MailFetchPosition position, bool includeRaw, CancellationToken cancellationToken = default)
    {
        if (!CanRead) return MailFetchResult.Empty(position);

        var folderName = _options.Folder ?? "INBOX";
        var key = MailReadHealth.KeyOf(_options.Imap, folderName);
        var credential = MailReadHealth.CredentialOf(_options.Imap);

        // Every row from a read names the mailbox. The user is the mailbox's own sign-in, which is what tells two mailboxes on one host apart.
        using var scope = logger?.BeginScope(new Dictionary<string, object> { ["MailHost"] = _options.Imap.Host, ["MailUser"] = _options.Imap.UserName, ["MailFolder"] = folderName });

        if (_health.DeferredUntil(key, credential) is { } until)
        {
            logger?.LogDebug("The mailbox is not tried until {RetryNotBefore:u}, after the server refused the sign-in.", until);

            return MailFetchResult.NotTried(position, $"Not tried until {until:u}: the server refused the sign-in, and the wait before the next attempt is not over.");
        }

        try
        {
            using var client = new ImapClient { Timeout = (int)_options.Timeout.TotalMilliseconds };

            await ConnectAsync(client, _options.Imap, cancellationToken);

            var folder = await client.GetFolderAsync(folderName, cancellationToken);

            // ReadOnly is the guarantee, not merely the intent: opening read-write is what lets a fetch set
            // \Seen as a side effect, which would hide the mail from the other application reading this
            // mailbox for its own domain.
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var rescanned = position.IsInvalidatedBy(folder.UidValidity);
            var from = rescanned ? 0 : position.LastUid;

            var uids = Newer(await folder.SearchAsync(SearchFor(from, _options.FirstReadSince), cancellationToken), from);

            var mails = new List<InboundMail>();
            var highest = from;

            foreach (var uid in uids.Take(MaxMessagesPerFetch))
            {
                var message = await folder.GetMessageAsync(uid, cancellationToken);

                mails.Add(await InboundMailReader.ReadAsync(message, senderVerifier ?? DkimSenderVerifier.Shared, uid.Id, includeRaw ? await RawAsync(message, cancellationToken) : null, cancellationToken));
                highest = uid.Id;
            }

            await client.DisconnectAsync(true, cancellationToken);

            if (_health.Succeeded(key, out var failedBefore))
                logger?.LogInformation("The mailbox could be read again, after {Failures} failed reads in a row.", failedBefore);

            return new MailFetchResult(new MailFetchPosition(folder.UidValidity, highest), mails, rescanned);
        }
        catch (Exception e)
        {
            var kind = MailReadHealth.Classify(e, cancellationToken);

            // Asked to stop is not a failure of the mailbox.
            if (kind == MailReadFailureKind.Cancelled)
            {
                logger?.LogDebug("The read of the mailbox was stopped.");

                return MailFetchResult.Failed(position, e.Message);
            }

            var outcome = _health.Failed(key, credential, kind);

            switch (kind)
            {
                case MailReadFailureKind.Refused:
                    logger?.Log(outcome.Level, e, "The mailbox refused the sign-in ({Failures} in a row). It is not tried again until {RetryNotBefore:u}.", outcome.Consecutive, outcome.RetryNotBefore);
                    break;
                case MailReadFailureKind.Transient when outcome.Level == LogLevel.Error:
                    logger?.Log(outcome.Level, e, "The mailbox could not be read {Failures} times in a row.", outcome.Consecutive);
                    break;
                case MailReadFailureKind.Transient:
                    logger?.Log(outcome.Level, e, "The mailbox could not be read ({Failures} in a row), and is read again at the next poll.", outcome.Consecutive);
                    break;
                default:
                    logger?.Log(outcome.Level, e, "The mailbox could not be read.");
                    break;
            }

            return MailFetchResult.Failed(position, e.Message);
        }
    }

    /// <remarks>
    /// Port zero asks MailKit for the protocol's default, which is why <see cref="MailServerOptions.Port"/>
    /// is nullable rather than carrying a guessed constant per protocol.
    /// </remarks>
    private static async Task ConnectAsync(IMailService client, MailServerOptions server, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(server.Host, server.Port ?? 0, SecurityOf(server), cancellationToken);

        if (!string.IsNullOrWhiteSpace(server.UserName))
        {
            await client.AuthenticateAsync(server.UserName, server.Password, cancellationToken);
        }
    }

    /// <summary>
    /// The UIDs above the read position. IMAP reads <c>n:*</c> as "n up to the highest UID", so when nothing
    /// newer exists the server still answers with the highest one, which would be read again on every poll.
    /// </summary>
    // IMAP's SINCE compares the date alone, so the time of day is dropped rather than half-honoured.
    internal static SearchQuery SearchFor(uint from, DateTime? firstReadSince)
    {
        var newer = SearchQuery.Uids(new UniqueIdRange(new UniqueId(from + 1), UniqueId.MaxValue));

        return from == 0 && firstReadSince.HasValue ? newer.And(SearchQuery.DeliveredAfter(firstReadSince.Value.Date)) : newer;
    }

    internal static IReadOnlyList<UniqueId> Newer(IEnumerable<UniqueId> uids, uint lastUid) => [.. uids.Where(x => x.Id > lastUid).OrderBy(x => x.Id)];

    /// <summary>Connects to the IMAP server and opens the folder read-only; null when that worked, otherwise why not.</summary>
    internal async Task<string> CheckReadAsync(CancellationToken cancellationToken)
    {
        if (!CanRead) return "No IMAP server is configured.";

        try
        {
            using var client = new ImapClient { Timeout = (int)_options.Timeout.TotalMilliseconds };
            await ConnectAsync(client, _options.Imap, cancellationToken);
            var folder = await client.GetFolderAsync(_options.Folder ?? "INBOX", cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return null;
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The mailbox could not be opened.");
            return e.Message;
        }
    }

    /// <summary>Connects to the SMTP server and signs in without sending anything; null when that worked, otherwise why not.</summary>
    internal async Task<string> CheckSendAsync(CancellationToken cancellationToken)
    {
        if (!_options.Smtp.IsConfigured) return "No SMTP server is configured.";

        try
        {
            using var client = new SmtpClient { Timeout = (int)_options.Timeout.TotalMilliseconds };
            await ConnectAsync(client, _options.Smtp, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return null;
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "The SMTP server could not be reached.");
            return e.Message;
        }
    }

    /// <remarks>
    /// <c>UseSsl</c> on a submission port (587, 25) means STARTTLS, required — what <c>System.Net.Mail</c> meant
    /// by it, and what SMTP on those ports speaks. Anywhere else it means TLS from the first byte (465, 993).
    /// Without it, TLS is still used whenever the server offers it.
    /// </remarks>
    internal static SecureSocketOptions SecurityOf(MailServerOptions server) => server.UseSsl
        ? server.Port is 587 or 25 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTlsWhenAvailable;

    private static async Task<byte[]> RawAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await message.WriteToAsync(stream, cancellationToken);
        return stream.ToArray();
    }
}
