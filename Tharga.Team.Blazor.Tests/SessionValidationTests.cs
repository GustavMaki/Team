using System.Security.Claims;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The per-request check of a server-side session (Tharga/Team#471): nothing at all without a policy, an old cookie adopted and never signed out, a claim that
/// names someone else's session refused, the host's limits counted from the right stamp, the newest sign-in winning a team, activity recorded rarely, and a
/// failing policy or store handled as configured. Modelled on <c>TeamEntryGuardTests</c>.
/// </summary>
/// <remarks>
/// Two <see cref="SessionEvaluator"/> with a cache of their own each over one <see cref="InMemorySessionStore"/> are two instances of the application. Times come
/// from <see cref="SessionClock"/>; teams and people are made up.
/// </remarks>
public class SessionValidationTests
{
    private const string Strict = "strict";
    private const string Loose = "loose";

    private readonly SessionClock _time = new();
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionPolicy _policy = new();
    private readonly SessionOptions _sessions = Options();
    private readonly List<AuditEntry> _audit = [];

    private SessionEvaluator Evaluator(bool withPolicy = true, InMemorySessionStore store = null, SessionCache cache = null)
        => new(cache ?? new SessionCache(_time), Wrap(_sessions), _time, withPolicy ? _policy : null, Sessions(store ?? _store, _time, _audit));

    /// <summary>A session signed in <paramref name="ago"/> ago whose last activity was <paramref name="idle"/> ago.</summary>
    private UserSession Started(string id = "s1", TimeSpan? ago = null, TimeSpan? idle = null, string team = null, string user = UserKey)
    {
        var signedIn = _time.Now - (ago ?? TimeSpan.FromMinutes(1));
        var session = Session(id, signedIn, user: user, team: team, lastActivity: _time.Now - (idle ?? ago ?? TimeSpan.FromMinutes(1)));
        _store.Put(session);
        return session;
    }

    private static SessionLimits Limits(TimeSpan? maxAge = null, TimeSpan? idle = null, bool single = false) => new() { MaxAge = maxAge, IdleTimeout = idle, SingleSession = single };

    // ---- nothing registered, or switched off

    [Fact]
    public async Task WithNoPolicyRegistered_NothingIsLookedUp_AndTheCookieIsUntouched()
    {
        var evaluator = Evaluator(withPolicy: false);

        var withSession = await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        var without = await evaluator.EvaluateAsync(WithoutSession(), null, Strict);

        Assert.False(evaluator.Active);
        Assert.Equal(SessionCheckOutcome.Valid, withSession.Outcome);
        Assert.Equal(SessionCheckOutcome.Valid, without.Outcome);
        Assert.Equal(0, _store.Calls);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task SwitchedOff_ThePolicyIsNotAsked_AndNothingIsStored()
    {
        _sessions.Enabled = false;
        var evaluator = Evaluator();

        var check = await evaluator.EvaluateAsync(WithoutSession(), null, Strict);

        Assert.False(evaluator.Active);
        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Empty(_policy.Asked);
        Assert.Equal(0, _store.Calls);
    }

    [Fact]
    public void ARegisteredPolicy_TurnsItOn()
    {
        Assert.True(Evaluator().Active);
    }

    // ---- adoption

    [Fact]
    public async Task ACookieWithoutASessionClaim_IsAdopted_NotSignedOut()
    {
        var check = await Evaluator().EvaluateAsync(WithoutSession(), null, Strict);

        Assert.Equal(SessionCheckOutcome.Adopted, check.Outcome);
        var claim = Assert.Single(check.Principal.FindAll(TeamClaimTypes.SessionId));
        Assert.Equal("Tharga.Team", claim.Issuer);
        var started = Assert.Single(_store.Started);
        Assert.Equal(claim.Value, started.SessionId);
        Assert.Equal((UserKey, Subject, SessionOrigin.Adopted), (started.UserKey, started.Subject, started.Origin));
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task AnAdoptedSession_CountsFromWhenTheCookieSignedIn_NotFromAdoption()
    {
        var authTime = _time.Now.AddHours(-3);
        var issued = new DateTimeOffset(_time.Now.AddHours(-2), TimeSpan.Zero);

        await Evaluator().EvaluateAsync(WithoutSession(extra: new Claim("auth_time", new DateTimeOffset(authTime, TimeSpan.Zero).ToUnixTimeSeconds().ToString())), issued, Strict);
        Assert.Equal(authTime, Assert.Single(_store.Started).SignedInAt);

        var second = new InMemorySessionStore();
        await Evaluator(store: second).EvaluateAsync(WithoutSession(), issued, Strict);
        Assert.Equal(issued.UtcDateTime, Assert.Single(second.Started).SignedInAt);
    }

    [Fact]
    public async Task AnAdoptedSession_ThatHasNoSignInTimeAtAll_StartsNow()
    {
        await Evaluator().EvaluateAsync(WithoutSession(), null, Strict);

        var started = Assert.Single(_store.Started);
        Assert.Equal((_time.Now, _time.Now), (started.SignedInAt, started.LastActivityAt));
    }

    [Fact]
    public async Task WhatTheSignInProved_IsKeptOnTheAdoptedSession()
    {
        await Evaluator().EvaluateAsync(WithoutSession(extra: [new Claim("idp", "entra"), new Claim("amr", "pwd"), new Claim("amr", "mfa")]), null, Strict);

        var started = Assert.Single(_store.Started);
        Assert.Equal("entra", started.IdentityProvider);
        Assert.Equal(["mfa", "pwd"], started.AuthenticationMethods.Order());
    }

    [Fact]
    public async Task WithAdoptionSwitchedOff_AnOldCookieIsLetThroughAsItIs()
    {
        _sessions.AdoptExistingCookies = false;

        var check = await Evaluator().EvaluateAsync(WithoutSession(), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Empty(_store.Started);
    }

    /// <summary>Turning a max age on can end older cookies at once, because an adopted session's age is the original sign-in's. Documented, and this is the proof.</summary>
    [Fact]
    public async Task AnAdoptedCookie_OlderThanTheMaxAge_IsEndedAtOnce()
    {
        _policy.Default = Limits(maxAge: TimeSpan.FromHours(8));
        var authTime = new DateTimeOffset(_time.Now.AddHours(-10), TimeSpan.Zero).ToUnixTimeSeconds().ToString();

        var check = await Evaluator().EvaluateAsync(WithoutSession(extra: new Claim("auth_time", authTime)), null, Strict);

        Assert.Equal((SessionCheckOutcome.Ended, SessionEndReason.MaxAge), (check.Outcome, check.Reason));
    }

    // ---- who the session belongs to

    [Fact]
    public async Task AValidSession_IsLetThrough_WithTheSamePrincipal()
    {
        Started("s1");
        var principal = WithSession("s1");

        var check = await Evaluator().EvaluateAsync(principal, null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Empty(_store.Started);
        Assert.Empty(_audit);
    }

    /// <summary>The claim travels in the data-protected cookie, so this is defence in depth: a claim naming another person's session is refused, and does not end theirs.</summary>
    [Fact]
    public async Task AClaimNamingAnotherPersonsSession_IsRejected_AndTheirSessionIsLeftAlone()
    {
        _store.Put(Session("theirs", _time.Now.AddMinutes(-5), user: "user-2"));

        var check = await Evaluator().EvaluateAsync(WithSession("theirs", subject: Subject), null, Strict);

        Assert.Equal(SessionCheckOutcome.Rejected, check.Outcome);
        Assert.Empty(_store.Ends);
        Assert.Null(_store.Get("theirs").EndedAt);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task AClaimNamingNoSession_IsRejected_NotAdopted()
    {
        var check = await Evaluator().EvaluateAsync(WithSession("gone"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Rejected, check.Outcome);
        Assert.Empty(_store.Started);
    }

    // ---- reading the session

    [Fact]
    public async Task TheSession_IsReadOncePerCacheWindow_PerInstance()
    {
        Started("s1");
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        _time.Advance(_sessions.CacheDuration - TimeSpan.FromSeconds(1));
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        Assert.Equal(1, _store.Reads);

        _time.Advance(TimeSpan.FromSeconds(2));
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        Assert.Equal(2, _store.Reads);
    }

    [Fact]
    public async Task ACacheDurationOfZero_ReadsEveryTime()
    {
        _sessions.CacheDuration = TimeSpan.Zero;
        Started("s1");
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(2, _store.Reads);
    }

    [Fact]
    public async Task ThePolicy_IsAskedWithTheSessionAndTheTeam_OncePerCacheWindowAndTeam()
    {
        Started("s1");
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        await evaluator.EvaluateAsync(WithSession("s1"), null, Loose);
        Assert.Equal([("s1", Strict), ("s1", Loose)], _policy.Asked);

        _time.Advance(_sessions.CacheDuration + TimeSpan.FromSeconds(1));
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        Assert.Equal(3, _policy.Asked.Count);
    }

    // ---- the limits

    [Fact]
    public async Task MaxAge_IsCountedFromTheSignIn_NotFromTheLastActivity()
    {
        _policy.Default = Limits(maxAge: TimeSpan.FromHours(8));
        Started("fresh", ago: TimeSpan.FromHours(7).Add(TimeSpan.FromMinutes(59)), idle: TimeSpan.FromSeconds(5));
        Started("old", ago: TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(1)), idle: TimeSpan.FromSeconds(5));

        var fresh = await Evaluator().EvaluateAsync(WithSession("fresh"), null, Strict);
        var old = await Evaluator().EvaluateAsync(WithSession("old"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, fresh.Outcome);
        Assert.Equal((SessionCheckOutcome.Ended, SessionEndReason.MaxAge), (old.Outcome, old.Reason));
        Assert.Equal(SessionEndReason.MaxAge, _store.Get("old").EndReason);
        Assert.Null(_store.Get("fresh").EndedAt);
    }

    [Fact]
    public async Task IdleTimeout_IsCountedFromTheLastActivity_NotFromTheSignIn()
    {
        _policy.Default = Limits(idle: TimeSpan.FromMinutes(2));
        Started("active", ago: TimeSpan.FromHours(5), idle: TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(59)));
        Started("idle", ago: TimeSpan.FromHours(5), idle: TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));

        var active = await Evaluator().EvaluateAsync(WithSession("active"), null, Strict);
        var idle = await Evaluator().EvaluateAsync(WithSession("idle"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, active.Outcome);
        Assert.Equal((SessionCheckOutcome.Ended, SessionEndReason.Idle), (idle.Outcome, idle.Reason));
    }

    [Fact]
    public async Task NoLimits_NeverEndAnything()
    {
        _policy.Default = SessionLimits.None;
        Started("s1", ago: TimeSpan.FromDays(400), idle: TimeSpan.FromDays(399));

        Assert.Equal(SessionCheckOutcome.Valid, (await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict)).Outcome);
    }

    [Fact]
    public async Task TheLimits_AreThoseOfTheTeamTheCallerIsIn()
    {
        _policy.For(Strict, Limits(idle: TimeSpan.FromMinutes(2))).For(Loose, SessionLimits.None);
        Started("s1", ago: TimeSpan.FromHours(1), idle: TimeSpan.FromMinutes(10));
        // A request is activity: with the default interval the Loose request would record it, and the Strict one would rightly find the person active.
        _sessions.ActivityInterval = TimeSpan.FromHours(1);

        var inLoose = await Evaluator().EvaluateAsync(WithSession("s1"), null, Loose);
        var inStrict = await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, inLoose.Outcome);
        Assert.Equal(SessionEndReason.Idle, inStrict.Reason);
    }

    /// <summary>A browser that is working inside a circuit sends no HTTP request, so the activity it reported to the store is what counts; the cached session may be older.</summary>
    [Fact]
    public async Task BeforeEndingASessionAsIdle_TheStoreIsReadAgain_AndActivityRecordedMeanwhileSavesIt()
    {
        _sessions.CacheDuration = TimeSpan.FromMinutes(10);
        _policy.Default = Limits(idle: TimeSpan.FromMinutes(2));
        Started("s1", ago: TimeSpan.FromMinutes(1), idle: TimeSpan.FromSeconds(10));
        var evaluator = Evaluator();
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);

        _time.Advance(TimeSpan.FromMinutes(3));
        _store.Put(_store.Get("s1") with { LastActivityAt = _time.Now.AddSeconds(-10) });
        var check = await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Empty(_store.Ends);
    }

    [Fact]
    public async Task ActivityOnAnotherInstance_KeepsTheSessionAlive()
    {
        _sessions.CacheDuration = TimeSpan.FromMinutes(10);
        _policy.Default = Limits(idle: TimeSpan.FromMinutes(2));
        Started("s1", ago: TimeSpan.FromSeconds(10));
        var one = Evaluator();
        var two = Evaluator();

        // Every ninety seconds, alternating: neither instance sees more than half of it.
        for (var step = 0; step < 6; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(90));
            var check = await (step % 2 == 0 ? one : two).EvaluateAsync(WithSession("s1"), null, Strict);
            Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        }

        Assert.Null(_store.Get("s1").EndedAt);
    }

    // ---- reject: once

    [Fact]
    public async Task AnEndedSession_IsAuditedOnce_NotPerRequest()
    {
        _policy.Default = Limits(idle: TimeSpan.FromMinutes(2));
        Started("s1", ago: TimeSpan.FromHours(1), idle: TimeSpan.FromMinutes(10));
        var evaluator = Evaluator();

        for (var i = 0; i < 3; i++)
        {
            var check = await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
            Assert.Equal(SessionCheckOutcome.Ended, check.Outcome);
        }

        var entry = Assert.Single(_audit);
        Assert.Equal(("auth", "session-ended"), (entry.Feature, entry.Action));
        Assert.Contains("Idle", entry.Metadata.Values);
    }

    /// <summary>Two instances reject the same request: the one whose end was conditional and lost does not audit.</summary>
    [Fact]
    public async Task TwoInstancesRejectingTheSameSession_AuditItOnce()
    {
        _policy.Default = Limits(maxAge: TimeSpan.FromHours(1));
        Started("s1", ago: TimeSpan.FromHours(2));

        await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);
        var other = await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Ended, other.Outcome);
        Assert.Single(_audit);
    }

    [Fact]
    public async Task ASessionEndedElsewhere_IsRejectedWithItsReason_AndNotAuditedHere()
    {
        _store.Put(Started("s1") with { EndedAt = _time.Now.AddMinutes(-1), EndReason = SessionEndReason.SignedOut });

        var check = await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal((SessionCheckOutcome.Ended, SessionEndReason.SignedOut), (check.Outcome, check.Reason));
        Assert.Empty(_audit);
    }

    // ---- the newest sign-in wins

    private (SessionEvaluator Evaluator, UserSession Older, UserSession Newer) TwoSignIns(string olderTeam = null, string newerTeam = null)
    {
        _policy.For(Strict, Limits(single: true)).For(Loose, SessionLimits.None);
        var older = Session("older", _time.Now.AddHours(-3), team: olderTeam);
        var newer = Session("newer", _time.Now.AddHours(-1), team: newerTeam);
        _store.Put(older);
        _store.Put(newer);
        return (Evaluator(), older, newer);
    }

    [Fact]
    public async Task TheNewerSignIn_EndsTheOlder_InThatTeamOnly()
    {
        var (evaluator, _, _) = TwoSignIns(olderTeam: Strict);
        _store.Put(Session("older-elsewhere", _time.Now.AddHours(-4), team: Loose));

        var check = await evaluator.EvaluateAsync(WithSession("newer"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Equal(SessionEndReason.NewerSignIn, _store.Get("older").EndReason);
        Assert.Null(_store.Get("older-elsewhere").EndedAt);
        Assert.Null(_store.Get("newer").EndedAt);
        Assert.Equal(Strict, _store.Get("newer").ActiveTeamKey);
    }

    [Fact]
    public async Task TheOlderSession_FindsItselfEnded_OnItsNextRequest_WithTheReason()
    {
        var (evaluator, _, _) = TwoSignIns(olderTeam: Strict);
        await evaluator.EvaluateAsync(WithSession("newer"), null, Strict);
        _time.Advance(_sessions.CacheDuration + TimeSpan.FromSeconds(1));

        var check = await Evaluator().EvaluateAsync(WithSession("older"), null, Strict);

        Assert.Equal((SessionCheckOutcome.Ended, SessionEndReason.NewerSignIn), (check.Outcome, check.Reason));
    }

    [Fact]
    public async Task EnteringATeamWhereANewerSessionIs_EndsTheSwitchingSession_AndAuditsItOnce()
    {
        var (evaluator, _, _) = TwoSignIns(newerTeam: Strict);

        var check = await evaluator.EvaluateAsync(WithSession("older"), null, Strict);

        Assert.Equal((SessionCheckOutcome.Ended, SessionEndReason.NewerSignIn), (check.Outcome, check.Reason));
        Assert.Null(_store.Get("newer").EndedAt);
        var entry = Assert.Single(_audit);
        Assert.Equal("session-ended", entry.Action);
    }

    [Fact]
    public async Task ASupersededSession_IsAudited_PerSession()
    {
        var (evaluator, _, _) = TwoSignIns(olderTeam: Strict);

        await evaluator.EvaluateAsync(WithSession("newer"), null, Strict);

        var entry = Assert.Single(_audit);
        Assert.Equal("session-superseded", entry.Action);
        Assert.Equal(Strict, entry.TeamKey);
    }

    [Fact]
    public async Task ATeamThatAllowsManySessions_IsNeverEnteredExclusively()
    {
        var (evaluator, _, _) = TwoSignIns(olderTeam: Loose);

        await evaluator.EvaluateAsync(WithSession("newer"), null, Loose);

        Assert.Empty(_store.Entries);
        Assert.Null(_store.Get("older").EndedAt);
    }

    [Fact]
    public async Task AlreadyInTheTeam_TheTeamIsNotEnteredAgainOnEveryRequest()
    {
        var (evaluator, _, _) = TwoSignIns();

        await evaluator.EvaluateAsync(WithSession("newer"), null, Strict);
        _time.Advance(_sessions.CacheDuration + TimeSpan.FromSeconds(1));
        await evaluator.EvaluateAsync(WithSession("newer"), null, Strict);
        await evaluator.EvaluateAsync(WithSession("newer"), null, Strict);

        Assert.Single(_store.Entries);
    }

    // ---- activity

    [Fact]
    public async Task Activity_IsRecordedOnlyOnceTheIntervalHasPassed_SinceTheCachedStamp()
    {
        _sessions.CacheDuration = TimeSpan.FromMinutes(10);
        Started("s1");
        _store.Put(_store.Get("s1") with { LastActivityAt = _time.Now });
        var evaluator = Evaluator();

        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        _time.Advance(TimeSpan.FromSeconds(30));
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        Assert.Empty(_store.Activity);

        _time.Advance(TimeSpan.FromSeconds(31));
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        var recorded = Assert.Single(_store.Activity);
        Assert.Equal(("s1", _time.Now, Strict, _sessions.ActivityInterval), recorded);

        _time.Advance(TimeSpan.FromSeconds(30));
        await evaluator.EvaluateAsync(WithSession("s1"), null, Strict);
        Assert.Single(_store.Activity);
    }

    // ---- a policy or a store that fails

    [Fact]
    public async Task APolicyThatThrows_IsAllowedByDefault_NothingIsEnded_AndNothingAudited()
    {
        _policy.Failure = new InvalidOperationException("the policy database is down");
        Started("s1");

        var check = await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Empty(_store.Ends);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task AStoreThatThrows_IsAllowedByDefault_ADatabaseBlipDoesNotSignEveryoneOut()
    {
        _store.Failure = new InvalidOperationException("the session database is down");

        var check = await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task AFailure_CanBeSetToDeny_WhichRefusesTheRequest_WithoutEndingTheSession()
    {
        _sessions.OnError = SessionErrorBehavior.Deny;
        _policy.Failure = new InvalidOperationException("down");
        Started("s1");

        var check = await Evaluator().EvaluateAsync(WithSession("s1"), null, Strict);

        Assert.Equal(SessionCheckOutcome.Rejected, check.Outcome);
        Assert.Null(_store.Get("s1").EndedAt);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task AnAdoptionThatFails_NeverCostsTheCaller_WhenAllowed()
    {
        _store.Failure = new InvalidOperationException("down");

        var check = await Evaluator().EvaluateAsync(WithoutSession(), null, Strict);

        Assert.Equal(SessionCheckOutcome.Valid, check.Outcome);
    }

    [Fact]
    public async Task ACancelledCheck_IsNotMistakenForAFailure()
    {
        _store.Failure = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Evaluator().EvaluateAsync(WithSession("s1"), null, Strict));
    }
}
