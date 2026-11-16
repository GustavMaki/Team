using System.Security.Claims;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Tests;

/// <summary>Doubles shared by the server-side session tests (Tharga/Team#471); the people, teams and times are made up and fixed.</summary>
internal static class SessionTestData
{
    /// <summary>When every test starts, by <see cref="SessionClock"/>.</summary>
    public static readonly DateTimeOffset Start = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public const string UserKey = "user-1";
    public const string Subject = "subject-1";

    public static SessionOptions Options(Action<SessionOptions> configure = null)
    {
        // Set out in full so no test leans on a default; SessionOptionsTests holds the defaults.
        var options = new SessionOptions
        {
            Enabled = true,
            CacheDuration = TimeSpan.FromSeconds(30),
            ActivityInterval = TimeSpan.FromMinutes(1),
            EndedPath = null,
            AdoptExistingCookies = true,
            OnError = SessionErrorBehavior.Allow,
            CookieScheme = "Cookies"
        };
        configure?.Invoke(options);
        return options;
    }

    public static IOptions<ThargaBlazorOptions> Wrap(SessionOptions sessions) => Microsoft.Extensions.Options.Options.Create(new ThargaBlazorOptions { Sessions = sessions });

    /// <summary>
    /// The domain service the evaluator reaches sessions through, over <paramref name="store"/>, auditing into <paramref name="audit"/> when given. The evaluator
    /// holds no store of its own: the ownership check and the audit-once rule are the domain's.
    /// </summary>
    public static UserSessionService Sessions(IUserSessionStore store, TimeProvider time, List<AuditEntry> audit = null)
        => new(store, new SessionUserService(), Mock.Of<ITeamPrincipalAccessor>(), time, auditLogger: audit == null ? null : CollectingAuditBackend.Composite(audit));

    /// <summary>A cookie principal carrying the library's session claim, as the cookie holds it after sign-in.</summary>
    public static ClaimsPrincipal WithSession(string sessionId, string subject = Subject, params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), new Claim(TeamClaimTypes.SessionId, sessionId, ClaimValueTypes.String, "Tharga.Team"), .. extra], "Test"));

    /// <summary>A cookie principal from before sessions existed: no session claim.</summary>
    public static ClaimsPrincipal WithoutSession(string subject = Subject, params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, subject), .. extra], "Test"));

    public static UserSession Session(string id, DateTime signedInAt, string user = UserKey, string subject = null, string team = null, DateTime? lastActivity = null, SessionOrigin origin = SessionOrigin.SignIn)
        => new()
        {
            SessionId = id,
            UserKey = user,
            Subject = subject ?? (user == UserKey ? Subject : $"subject-{user}"),
            SignedInAt = signedInAt,
            Origin = origin,
            LastActivityAt = lastActivity ?? signedInAt,
            ActiveTeamKey = team
        };
}

internal sealed class SessionClock : TimeProvider
{
    private DateTimeOffset _now = SessionTestData.Start;

    public override DateTimeOffset GetUtcNow() => _now;

    public DateTime Now => _now.UtcDateTime;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class CollectingAuditBackend(List<AuditEntry> entries) : IAuditLogger
{
    public void Log(AuditEntry entry) => entries.Add(entry);

    public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());

    public static CompositeAuditLogger Composite(List<AuditEntry> entries) => new([new CollectingAuditBackend(entries)], Microsoft.Extensions.Options.Options.Create(new AuditOptions()));
}

/// <summary>Answers the one-argument call only; the interface's default for the two-argument one forwards to it, so either finds the user.</summary>
internal sealed class SessionUserService(string key = SessionTestData.UserKey) : IUserService
{
    private sealed record User(string Key) : IUser
    {
        public string Identity => "alice";
        public string EMail => "alice@example.com";
    }

    public Task<IUser> GetCurrentUserAsync(ClaimsPrincipal claimsPrincipal = null) => Task.FromResult<IUser>(key == null ? null : new User(key));

    public IAsyncEnumerable<IUser> GetAsync() => throw new NotSupportedException();

    public Task SetUserNameAsync(string userKey, string name) => Task.CompletedTask;

    public Task SeedUserNameAsync(string userKey, string name) => Task.CompletedTask;
}

/// <summary>The host's numbers, per team, counting how often it is asked and optionally failing.</summary>
internal sealed class TestSessionPolicy : ISessionPolicy
{
    private readonly Dictionary<string, SessionLimits> _byTeam = new();

    public SessionLimits Default { get; set; } = SessionLimits.None;

    public Exception Failure { get; set; }

    public List<(string SessionId, string TeamKey)> Asked { get; } = [];

    public TestSessionPolicy For(string teamKey, SessionLimits limits)
    {
        _byTeam[teamKey] = limits;
        return this;
    }

    public Task<SessionLimits> GetLimitsAsync(UserSession session, string teamKey, CancellationToken cancellationToken = default)
    {
        Asked.Add((session.SessionId, teamKey));
        if (Failure != null) throw Failure;

        return Task.FromResult(teamKey != null && _byTeam.TryGetValue(teamKey, out var limits) ? limits : Default);
    }
}

/// <summary>
/// The store's contract in memory, so two evaluators ("instances") can share it: an end that is true once, activity throttled by the stored stamp, and
/// entering a team exclusively with the newest sign-in winning. It is the contract <c>UserSessionStoreTests</c> holds the real store to.
/// </summary>
internal sealed class InMemorySessionStore : IUserSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, UserSession> _sessions = new();

    public Exception Failure { get; set; }

    public List<UserSession> Started { get; } = [];

    public int Reads { get; private set; }

    public List<(string SessionId, SessionEndReason Reason)> Ends { get; } = [];

    public List<(string SessionId, DateTime At, string TeamKey, TimeSpan MinInterval)> Activity { get; } = [];

    public List<(string SessionId, string TeamKey)> Entries { get; } = [];

    public int Calls { get; private set; }

    public UserSession Get(string id)
    {
        lock (_gate) return _sessions.GetValueOrDefault(id);
    }

    public void Put(UserSession session)
    {
        lock (_gate) _sessions[session.SessionId] = session;
    }

    private void Enter()
    {
        Calls++;
        if (Failure != null) throw Failure;
    }

    public Task StartAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Started.Add(session);
            _sessions[session.SessionId] = session;
        }

        return Task.CompletedTask;
    }

    public Task<UserSession> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Reads++;
            return Task.FromResult(_sessions.GetValueOrDefault(sessionId));
        }
    }

    public Task<bool> RecordActivityAsync(string sessionId, DateTime at, string teamKey, TimeSpan minInterval, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Activity.Add((sessionId, at, teamKey, minInterval));
            if (!_sessions.TryGetValue(sessionId, out var session) || session.EndedAt != null || at - session.LastActivityAt < minInterval || at < session.LastActivityAt) return Task.FromResult(false);

            _sessions[sessionId] = session with { LastActivityAt = at, ActiveTeamKey = teamKey };
            return Task.FromResult(true);
        }
    }

    public Task<bool> EndAsync(string sessionId, SessionEndReason reason, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Ends.Add((sessionId, reason));
            if (!_sessions.TryGetValue(sessionId, out var session) || session.EndedAt != null) return Task.FromResult(false);

            _sessions[sessionId] = session with { EndedAt = at, EndReason = reason };
            return Task.FromResult(true);
        }
    }

    public Task<int> EndOthersAsync(string userKey, string exceptSessionId, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            var others = _sessions.Values.Where(x => x.UserKey == userKey && x.SessionId != exceptSessionId && x.EndedAt == null).ToArray();
            foreach (var other in others) _sessions[other.SessionId] = other with { EndedAt = at, EndReason = SessionEndReason.Ended };
            return Task.FromResult(others.Length);
        }
    }

    public Task<SessionTeamEntry> EnterTeamExclusiveAsync(string sessionId, string teamKey, DateTime at, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Enter();
            Entries.Add((sessionId, teamKey));
            var caller = _sessions[sessionId];
            var inTeam = _sessions.Values.Where(x => x.UserKey == caller.UserKey && x.SessionId != sessionId && x.EndedAt == null && x.ActiveTeamKey == teamKey).ToArray();

            if (inTeam.Any(x => x.SignedInAt > caller.SignedInAt))
            {
                _sessions[sessionId] = caller with { EndedAt = at, EndReason = SessionEndReason.NewerSignIn };
                return Task.FromResult(new SessionTeamEntry([], true));
            }

            foreach (var older in inTeam) _sessions[older.SessionId] = older with { EndedAt = at, EndReason = SessionEndReason.NewerSignIn };
            _sessions[sessionId] = caller with { ActiveTeamKey = teamKey };
            return Task.FromResult(new SessionTeamEntry(inTeam, false));
        }
    }
}
