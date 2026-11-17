using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Email;

/// <summary>Why a read of a mailbox failed, as far as what to do about it goes.</summary>
internal enum MailReadFailureKind
{
    /// <summary>The caller asked it to stop; nothing is wrong.</summary>
    Cancelled,

    /// <summary>A timeout or a dropped connection. The next read normally works.</summary>
    Transient,

    /// <summary>The server refused the sign-in. Reading again with the same credentials will be refused again.</summary>
    Refused,

    /// <summary>Anything else.</summary>
    Other
}

/// <summary>What a failed read means for the log and for the next attempt.</summary>
/// <param name="Level">How loudly to log it.</param>
/// <param name="Consecutive">How many reads in a row have failed this way, this one included.</param>
/// <param name="RetryNotBefore">When the mailbox may be tried again, or null when the next poll may.</param>
internal sealed record MailReadFailureOutcome(LogLevel Level, int Consecutive, DateTimeOffset? RetryNotBefore);

/// <summary>
/// What reading each mailbox has been like lately: how many reads in a row failed, and whether the server is refusing the sign-in.
/// </summary>
/// <remarks>
/// <b>Why it is kept apart from the client.</b> A mail client is created and disposed for each poll, so it cannot see a streak; this is
/// shared by mailbox, so a mailbox read once a minute on every instance is told apart from one read now and then.
/// <para>
/// A transient failure (a timeout, a dropped connection) is a Warning, and an Error only after <see cref="ErrorAfter"/> in a row and then
/// every <see cref="ErrorEvery"/> after that, so a real outage is still loud and a blip is not. A refused sign-in is an Error each time it
/// is tried, but is not tried every poll: the wait doubles from a minute up to an hour, and a changed password is tried at once.
/// </para>
/// </remarks>
internal sealed class MailReadHealth(TimeProvider time = null)
{
    /// <summary>The process-wide record, which is what the mail clients of an application share.</summary>
    public static MailReadHealth Shared { get; } = new();

    /// <summary>How many transient failures in a row are an Error.</summary>
    internal const int ErrorAfter = 5;

    /// <summary>After that, how many more in a row until the next Error.</summary>
    internal const int ErrorEvery = 30;

    internal static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.Ordinal);

    /// <summary>Which mailbox a read is of: where it is, who signs in, and which folder.</summary>
    public static string KeyOf(MailServerOptions server, string folder) => $"{server.Host}:{server.Port}|{server.UserName}|{folder}";

    /// <summary>A stand-in for the password that tells a changed one from the same, without keeping it.</summary>
    public static string CredentialOf(MailServerOptions server)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.Password ?? string.Empty)));

    /// <summary>
    /// When the mailbox may next be tried, if that is not now: only after a refused sign-in, and not for a password that has changed since.
    /// </summary>
    public DateTimeOffset? DeferredUntil(string key, string credential)
    {
        if (!_states.TryGetValue(key, out var state)) return null;

        lock (state)
        {
            if (state.Credential != credential)
            {
                _states.TryRemove(key, out _);
                return null;
            }

            return state.RetryNotBefore is { } until && until > _time.GetUtcNow() ? until : null;
        }
    }

    /// <summary>Records a failed read and says how to treat it.</summary>
    public MailReadFailureOutcome Failed(string key, string credential, MailReadFailureKind kind)
    {
        var state = _states.GetOrAdd(key, _ => new State());

        lock (state)
        {
            if (state.Credential != credential || state.Kind != kind)
            {
                state.Consecutive = 0;
                state.RetryNotBefore = null;
            }

            state.Credential = credential;
            state.Kind = kind;
            state.Consecutive++;

            switch (kind)
            {
                case MailReadFailureKind.Refused:
                    var wait = TimeSpan.FromTicks(Math.Min(MaxBackoff.Ticks, FirstBackoff.Ticks << Math.Min(state.Consecutive - 1, 12)));
                    state.RetryNotBefore = _time.GetUtcNow() + wait;

                    return new MailReadFailureOutcome(LogLevel.Error, state.Consecutive, state.RetryNotBefore);

                case MailReadFailureKind.Transient:
                    var loud = state.Consecutive == ErrorAfter || (state.Consecutive > ErrorAfter && (state.Consecutive - ErrorAfter) % ErrorEvery == 0);

                    return new MailReadFailureOutcome(loud ? LogLevel.Error : LogLevel.Warning, state.Consecutive, null);

                default:
                    return new MailReadFailureOutcome(LogLevel.Error, state.Consecutive, null);
            }
        }
    }

    /// <summary>Records a read that worked. True when the mailbox had been failing, so that a recovery can be said once.</summary>
    public bool Succeeded(string key, out int failuresBefore)
    {
        failuresBefore = 0;
        if (!_states.TryRemove(key, out var state)) return false;

        lock (state) failuresBefore = state.Consecutive;

        return failuresBefore > 0;
    }

    /// <summary>What kind of failure an exception is, looking through what it wraps.</summary>
    /// <remarks>
    /// A timeout reaches the caller as a <c>TaskCanceledException</c> whose cause is inside it, so the chain is read, not the outer type alone.
    /// A cancellation the caller did not ask for is a timeout, not a request to stop.
    /// </remarks>
    public static MailReadFailureKind Classify(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) return MailReadFailureKind.Cancelled;

        var chain = Chain(exception).ToArray();

        if (chain.Any(x => x is MailKit.Security.AuthenticationException)) return MailReadFailureKind.Refused;

        return chain.Any(x => x is OperationCanceledException or TimeoutException or IOException or SocketException or ImapProtocolException or ServiceNotConnectedException)
            ? MailReadFailureKind.Transient
            : MailReadFailureKind.Other;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var x = exception; x != null; x = x.InnerException) yield return x;
    }

    private sealed class State
    {
        public string Credential { get; set; }
        public MailReadFailureKind Kind { get; set; }
        public int Consecutive { get; set; }
        public DateTimeOffset? RetryNotBefore { get; set; }
    }
}
