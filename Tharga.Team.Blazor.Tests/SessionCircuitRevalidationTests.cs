using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A live circuit and the server-side session (Tharga/Team#471): the revalidation loop ends the circuit when the session has ended, beside the disabled-user check
/// it sits next to, in the same fail-open way. A circuit makes no HTTP request, so without this a session ended elsewhere would live on in an open tab.
/// </summary>
public class SessionCircuitRevalidationTests
{
    private readonly SessionClock _time = new();
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionPolicy _policy = new();
    private readonly List<AuditEntry> _audit = [];

    private IServiceProvider Services(bool withPolicy = true, bool withEvaluator = true)
    {
        var services = new ServiceCollection();
        if (withEvaluator)
        {
            services.AddSingleton(new SessionEvaluator(new SessionCache(_time), Wrap(Options()), _time, withPolicy ? _policy : null, Sessions(_store, _time, _audit)));
        }

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal InTeam(string sessionId, string team = "strict")
        => WithSession(sessionId, extra: new Claim(TeamClaimTypes.SelectedTeamKey, team));

    [Fact]
    public async Task ASessionThatHasEnded_EndsTheCircuit()
    {
        _store.Put(Session("s1", _time.Now.AddHours(-2)) with { EndedAt = _time.Now.AddMinutes(-1), EndReason = SessionEndReason.Idle });

        Assert.True(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(), InTeam("s1")));
    }

    [Fact]
    public async Task ASessionThatIsStillGoing_KeepsTheCircuit()
    {
        _store.Put(Session("s1", _time.Now.AddMinutes(-5)));

        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(), InTeam("s1")));
    }

    [Fact]
    public async Task ASessionOverItsLimit_EndsTheCircuit_AndIsAuditedOnce()
    {
        _policy.Default = new SessionLimits { IdleTimeout = TimeSpan.FromMinutes(2) };
        _store.Put(Session("s1", _time.Now.AddHours(-2), lastActivity: _time.Now.AddMinutes(-30)));
        var services = Services();

        Assert.True(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(services, InTeam("s1")));
        Assert.True(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(services, InTeam("s1")));

        Assert.Equal("session-ended", Assert.Single(_audit).Action);
    }

    [Fact]
    public async Task TheLimitsAreThoseOfTheTeamTheCircuitIsIn()
    {
        _policy.For("strict", new SessionLimits { IdleTimeout = TimeSpan.FromMinutes(2) });
        _store.Put(Session("s1", _time.Now.AddHours(-2), lastActivity: _time.Now.AddMinutes(-30)));

        await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(), InTeam("s1", "strict"));

        Assert.Equal([("s1", "strict")], _policy.Asked);
    }

    [Fact]
    public async Task WithNoPolicy_TheCircuitIsLeftAlone_AndNothingIsRead()
    {
        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(withPolicy: false), InTeam("s1")));
        Assert.Equal(0, _store.Calls);
    }

    [Fact]
    public async Task WithNoSessionServicesAtAll_TheCircuitIsLeftAlone()
    {
        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(withEvaluator: false), InTeam("s1")));
    }

    /// <summary>Fail-open, like the disabled-user check beside it: a database blip must not sign every open tab out at once.</summary>
    [Fact]
    public async Task AStoreThatThrows_DoesNotEndAnyCircuit()
    {
        _store.Failure = new InvalidOperationException("the session database is down");

        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(), InTeam("s1")));
    }

    /// <summary>It never adopts: a circuit's cookie was validated when the page loaded, and adoption is the cookie's business (needs the properties and a renewal).</summary>
    [Fact]
    public async Task ACircuitWithNoSession_IsLeftAlone_NotAdopted()
    {
        Assert.False(await TeamRevalidatingAuthenticationStateProvider.IsSessionEndedAsync(Services(), WithoutSession()));
        Assert.Empty(_store.Started);
    }
}
