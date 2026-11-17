using System.Net.Sockets;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// A transient read failure is a warning and not an error, a rejected password is not retried at every poll, and every row names the mailbox
/// (Tharga/Team#510).
/// </summary>
public class MailReadHealthTests
{
    private const string Key = "imap.example.com:993|support@example.com|INBOX";
    private const string Credential = "cred-1";

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (MailReadHealth Health, ManualTime Time) Build()
    {
        var time = new ManualTime();

        return (new MailReadHealth(time), time);
    }

    // --- what kind of failure it is ---

    [Fact]
    public void ATimeout_ThatReachesTheCallerAsACancellation_IsTransient_NotAStop()
    {
        var timeout = new TaskCanceledException("The read operation failed, see inner exception", new IOException("timed out"));

        Assert.Equal(MailReadFailureKind.Transient, MailReadHealth.Classify(timeout, CancellationToken.None));
    }

    [Fact]
    public void ACancellationTheCallerAskedFor_IsNotAFailureOfTheMailbox()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Equal(MailReadFailureKind.Cancelled, MailReadHealth.Classify(new OperationCanceledException(), source.Token));
    }

    [Fact]
    public void ADroppedOrSlowConnection_IsTransient()
    {
        Exception[] dropped = [new ImapProtocolException("The IMAP server has unexpectedly disconnected"), new IOException("dropped"), new TimeoutException("slow"), new SocketException()];

        Assert.All(dropped, x => Assert.Equal(MailReadFailureKind.Transient, MailReadHealth.Classify(x, CancellationToken.None)));
    }

    [Fact]
    public void ARejectedPassword_IsRefused_EvenWrappedInsideAnother()
    {
        var refused = new MailKit.Security.AuthenticationException("Invalid credentials");

        Assert.Equal(MailReadFailureKind.Refused, MailReadHealth.Classify(refused, CancellationToken.None));
        Assert.Equal(MailReadFailureKind.Refused, MailReadHealth.Classify(new InvalidOperationException("outer", refused), CancellationToken.None));
    }

    [Fact]
    public void AnythingElse_IsOther()
        => Assert.Equal(MailReadFailureKind.Other, MailReadHealth.Classify(new InvalidOperationException("x"), CancellationToken.None));

    // --- how loud a failure is ---

    [Fact]
    public void ATransientFailure_IsAWarning_AndAnErrorOnlyAfterSeveralInARow()
    {
        var (health, _) = Build();

        var levels = Enumerable.Range(1, MailReadHealth.ErrorAfter)
            .Select(_ => health.Failed(Key, Credential, MailReadFailureKind.Transient).Level)
            .ToArray();

        Assert.All(levels.Take(MailReadHealth.ErrorAfter - 1), x => Assert.Equal(LogLevel.Warning, x));
        Assert.Equal(LogLevel.Error, levels[^1]);
    }

    [Fact]
    public void AnOutage_IsNotAnErrorAtEveryPoll_ButRemindsNowAndThen()
    {
        var (health, _) = Build();
        var levels = Enumerable.Range(1, MailReadHealth.ErrorAfter + MailReadHealth.ErrorEvery)
            .Select(_ => health.Failed(Key, Credential, MailReadFailureKind.Transient).Level)
            .ToArray();

        Assert.Equal(2, levels.Count(x => x == LogLevel.Error));
        Assert.Equal(LogLevel.Error, levels[^1]);
    }

    [Fact]
    public void AReadThatWorks_StartsTheCountOver_AndSaysItRecovered()
    {
        var (health, _) = Build();
        for (var i = 0; i < 3; i++) health.Failed(Key, Credential, MailReadFailureKind.Transient);

        Assert.True(health.Succeeded(Key, out var failures));
        Assert.Equal(3, failures);
        Assert.Equal(1, health.Failed(Key, Credential, MailReadFailureKind.Transient).Consecutive);
        Assert.False(health.Succeeded("another mailbox", out _));
    }

    [Fact]
    public void AFailureOfAnotherKind_StartsItsOwnCount()
    {
        var (health, _) = Build();
        health.Failed(Key, Credential, MailReadFailureKind.Transient);
        health.Failed(Key, Credential, MailReadFailureKind.Transient);

        Assert.Equal(1, health.Failed(Key, Credential, MailReadFailureKind.Refused).Consecutive);
    }

    [Fact]
    public void AnUnexpectedFailure_StaysAnError()
    {
        var (health, _) = Build();

        Assert.Equal(LogLevel.Error, health.Failed(Key, Credential, MailReadFailureKind.Other).Level);
    }

    // --- a rejected password is not retried at every poll ---

    [Fact]
    public void ARefusedSignIn_IsAnError_AndNotTriedAgainAtOnce()
    {
        var (health, time) = Build();

        var outcome = health.Failed(Key, Credential, MailReadFailureKind.Refused);

        Assert.Equal(LogLevel.Error, outcome.Level);
        Assert.Equal(time.Now + MailReadHealth.FirstBackoff, outcome.RetryNotBefore);
        Assert.Equal(outcome.RetryNotBefore, health.DeferredUntil(Key, Credential));
    }

    [Fact]
    public void TheWait_DoublesWithEachRefusal_UpToAnHour()
    {
        var (health, time) = Build();
        var waits = new List<TimeSpan>();

        for (var i = 0; i < 10; i++)
        {
            var outcome = health.Failed(Key, Credential, MailReadFailureKind.Refused);
            waits.Add(outcome.RetryNotBefore!.Value - time.Now);
            time.Now = outcome.RetryNotBefore.Value;
        }

        Assert.Equal([1, 2, 4, 8, 16, 32, 60, 60, 60, 60], waits.Select(x => (int)x.TotalMinutes));
    }

    [Fact]
    public void WhenTheWaitIsOver_TheMailboxIsTriedAgain()
    {
        var (health, time) = Build();
        health.Failed(Key, Credential, MailReadFailureKind.Refused);

        time.Now += MailReadHealth.FirstBackoff + TimeSpan.FromSeconds(1);

        Assert.Null(health.DeferredUntil(Key, Credential));
    }

    [Fact]
    public void ANewPassword_IsTriedAtOnce_NotAfterTheWait()
    {
        var (health, _) = Build();
        health.Failed(Key, Credential, MailReadFailureKind.Refused);

        Assert.Null(health.DeferredUntil(Key, "a-changed-password"));
        Assert.Equal(1, health.Failed(Key, "a-changed-password", MailReadFailureKind.Refused).Consecutive);
    }

    [Fact]
    public void AnotherMailbox_IsNotHeldBackByThisOne()
    {
        var (health, _) = Build();
        health.Failed(Key, Credential, MailReadFailureKind.Refused);

        Assert.Null(health.DeferredUntil("imap.example.com:993|other@example.com|INBOX", Credential));
    }

    [Fact]
    public void ThePasswordStandIn_ReadsAsNothingOfThePassword()
    {
        var server = new MailServerOptions { Password = "app-password-123" };

        Assert.DoesNotContain("app-password-123", MailReadHealth.CredentialOf(server));
        Assert.NotEqual(MailReadHealth.CredentialOf(server), MailReadHealth.CredentialOf(new MailServerOptions { Password = "other" }));
    }

    // --- the client ---

    private static MailOptions Options(string user = "support@example.com", string password = "pw")
    {
        var options = new MailOptions { Timeout = TimeSpan.FromSeconds(2), Folder = "INBOX" };
        options.Imap.Host = "127.0.0.1";
        options.Imap.Port = 1;
        options.Imap.UseSsl = false;
        options.Imap.UserName = user;
        options.Imap.Password = password;

        return options;
    }

    [Fact]
    public async Task AMailboxThatCannotBeReached_IsAWarning_NotAnError_AndItsRowNamesTheMailbox()
    {
        var logger = new ScopedLogger();
        var health = new MailReadHealth();

        var result = await new MailClient(Microsoft.Extensions.Options.Options.Create(Options()), logger, null, health).FetchAsync(new MailFetchPosition(7, 42), TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.False(result.Deferred);
        var failure = Assert.Single(logger.Entries, x => x.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, failure.Level);
        Assert.Contains(failure.Scopes, x => x.TryGetValue("MailHost", out var host) && (string)host == "127.0.0.1");
        Assert.Contains(failure.Scopes, x => x.TryGetValue("MailUser", out var user) && (string)user == "support@example.com");
        Assert.Contains(failure.Scopes, x => x.TryGetValue("MailFolder", out var folder) && (string)folder == "INBOX");
    }

    [Fact]
    public async Task AfterARefusedSignIn_TheMailboxIsNotTried_AndNothingIsLoggedAboveDebug()
    {
        var options = Options();
        var logger = new ScopedLogger();
        var health = new MailReadHealth();
        health.Failed(MailReadHealth.KeyOf(options.Imap, "INBOX"), MailReadHealth.CredentialOf(options.Imap), MailReadFailureKind.Refused);

        var position = new MailFetchPosition(7, 42);
        var result = await new MailClient(Microsoft.Extensions.Options.Options.Create(options), logger, null, health).FetchAsync(position, TestContext.Current.CancellationToken);

        Assert.True(result.Deferred);
        Assert.Equal(position, result.Position);
        Assert.Empty(result.Mails);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task AChangedPassword_IsTriedAtOnce()
    {
        var before = Options(password: "old");
        var health = new MailReadHealth();
        health.Failed(MailReadHealth.KeyOf(before.Imap, "INBOX"), MailReadHealth.CredentialOf(before.Imap), MailReadFailureKind.Refused);

        var result = await new MailClient(Microsoft.Extensions.Options.Options.Create(Options(password: "new")), null, null, health).FetchAsync(new MailFetchPosition(0, 0), TestContext.Current.CancellationToken);

        Assert.False(result.Deferred);
    }

    [Fact]
    public async Task AReadTheCallerStopped_IsNotLoggedAsAFailure()
    {
        var logger = new ScopedLogger();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var result = await new MailClient(Microsoft.Extensions.Options.Options.Create(Options()), logger, null, new MailReadHealth()).FetchAsync(new MailFetchPosition(0, 0), source.Token);

        Assert.NotNull(result.Error);
        Assert.DoesNotContain(logger.Entries, x => x.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task TheLogRows_NeverNameThePassword()
    {
        var logger = new ScopedLogger();

        await new MailClient(Microsoft.Extensions.Options.Options.Create(Options(password: "app-password-123")), logger, null, new MailReadHealth()).FetchAsync(new MailFetchPosition(0, 0), TestContext.Current.CancellationToken);

        Assert.All(logger.Entries, x => Assert.DoesNotContain("app-password-123", x.Message + string.Join(",", x.Scopes.SelectMany(s => s.Values))));
    }

    private sealed class ScopedLogger : ILogger<MailClient>
    {
        private readonly List<Dictionary<string, object>> _open = [];

        public List<(LogLevel Level, string Message, List<Dictionary<string, object>> Scopes)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var scope = ((IEnumerable<KeyValuePair<string, object>>)state).ToDictionary(x => x.Key, x => x.Value);
            _open.Add(scope);

            return new Closer(() => _open.Remove(scope));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), [.. _open]));

        private sealed class Closer(Action close) : IDisposable
        {
            public void Dispose() => close();
        }
    }
}
