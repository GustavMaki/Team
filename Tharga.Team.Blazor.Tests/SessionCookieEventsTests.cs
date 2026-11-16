using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The session check as the cookie runs it (Tharga/Team#471): chained into the cookie scheme's <c>OnValidatePrincipal</c> by <c>AddThargaTeamBlazor</c>, so a host that
/// signs in by its own means is covered too, other schemes are not touched, and with no policy the inner handler runs and nothing else.
/// </summary>
/// <remarks>
/// The handler is taken from the options the host would run with and invoked on a <see cref="CookieValidatePrincipalContext"/>, with the store, the policy and the
/// clock replaced by test doubles. The check runs <b>before</b> the inner handler, which an ended session never reaches (assumption: a host's handler is not asked
/// to vouch for a principal that is already refused).
/// </remarks>
public class SessionCookieEventsTests
{
    private readonly SessionClock _time = new();
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionPolicy _policy = new();
    private readonly List<AuditEntry> _audit = [];
    private readonly RecordingAuthenticationService _auth = new();
    private int _innerCalls;

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public List<string> SignOuts { get; } = [];

        public Task SignOutAsync(HttpContext context, string scheme, AuthenticationProperties properties)
        {
            SignOuts.Add(scheme);
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string scheme) => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string scheme, AuthenticationProperties properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string scheme, AuthenticationProperties properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string scheme, ClaimsPrincipal principal, AuthenticationProperties properties) => Task.CompletedTask;
    }

    private ServiceProvider Host(bool withPolicy = true, Action<SessionOptions> sessions = null, bool withInner = true, string innerScheme = "Cookies")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddSingleton<TimeProvider>(_time);

        // A host's own handler, registered before the library chains into the scheme, as AddThargaAuth's OIDC wrapper is.
        if (withInner) services.Configure<CookieAuthenticationOptions>(innerScheme, o => o.Events.OnValidatePrincipal = _ =>
        {
            _innerCalls++;
            return Task.CompletedTask;
        });

        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
            o.Sessions = Options(sessions);
        });

        // Registered last, so these are the ones resolved.
        services.AddSingleton<IUserSessionStore>(_store);
        if (withPolicy) services.AddSingleton<ISessionPolicy>(_policy);
        services.AddScoped<IUserService>(_ => new SessionUserService());
        services.AddSingleton(CollectingAuditBackend.Composite(_audit));
        services.AddSingleton<IAuthenticationService>(_auth);

        return services.BuildServiceProvider();
    }

    private static async Task<(CookieValidatePrincipalContext Context, HttpContext Http)> ValidateAsync(ServiceProvider provider, ClaimsPrincipal principal, string scheme = "Cookies", string team = null, DateTimeOffset? issued = null)
    {
        var scope = provider.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (team != null) http.Request.Headers.Cookie = $"selected_team_id={team}";

        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = issued }, scheme);
        var context = new CookieValidatePrincipalContext(http, new AuthenticationScheme(scheme, null, typeof(CookieAuthenticationHandler)), options, ticket);

        await options.Events.OnValidatePrincipal(context);
        return (context, http);
    }

    // ---- nothing registered

    [Fact]
    public async Task WithNoPolicyRegistered_TheInnerHandlerRuns_AndNothingElse()
    {
        using var provider = Host(withPolicy: false);
        var principal = WithoutSession();

        var (context, _) = await ValidateAsync(provider, principal);

        Assert.Equal(1, _innerCalls);
        Assert.Same(principal, context.Principal);
        Assert.False(context.ShouldRenew);
        Assert.Equal(0, _store.Calls);
        Assert.Empty(_auth.SignOuts);
        Assert.Empty(_audit);
    }

    [Fact]
    public async Task WithNoPolicyAndNoInnerHandler_NothingBreaks()
    {
        using var provider = Host(withPolicy: false, withInner: false);

        var (context, _) = await ValidateAsync(provider, WithSession("s1"));

        Assert.NotNull(context.Principal);
        Assert.Equal(0, _store.Calls);
    }

    // ---- a valid session, and an old cookie

    [Fact]
    public async Task AValidSession_PassesOnToTheInnerHandler_Untouched()
    {
        _store.Put(Session("s1", _time.Now.AddMinutes(-5), lastActivity: _time.Now));
        using var provider = Host();
        var principal = WithSession("s1");

        var (context, _) = await ValidateAsync(provider, principal);

        Assert.Equal(1, _innerCalls);
        Assert.Same(principal, context.Principal);
        Assert.False(context.ShouldRenew);
        Assert.Empty(_auth.SignOuts);
    }

    [Fact]
    public async Task ACookieWithoutASessionClaim_IsAdopted_RenewedWithTheClaim_NotSignedOut()
    {
        using var provider = Host();

        var (context, _) = await ValidateAsync(provider, WithoutSession(), issued: new DateTimeOffset(_time.Now.AddDays(-2), TimeSpan.Zero));

        var claim = Assert.Single(context.Principal.FindAll(TeamClaimTypes.SessionId));
        Assert.Equal("Tharga.Team", claim.Issuer);
        Assert.True(context.ShouldRenew);
        Assert.Equal(SessionOrigin.Adopted, Assert.Single(_store.Started).Origin);
        Assert.Equal(1, _innerCalls);
        Assert.Empty(_auth.SignOuts);
        Assert.Empty(_audit);
    }

    // ---- an ended session

    [Fact]
    public async Task AnEndedSession_RejectsThePrincipal_SignsTheCookieOut_AndAuditsOnce()
    {
        _policy.Default = new SessionLimits { IdleTimeout = TimeSpan.FromMinutes(2) };
        _store.Put(Session("s1", _time.Now.AddHours(-2), lastActivity: _time.Now.AddMinutes(-30)));
        using var provider = Host();

        var (first, _) = await ValidateAsync(provider, WithSession("s1"));
        var (second, _) = await ValidateAsync(provider, WithSession("s1"));

        Assert.Null(first.Principal);
        Assert.Null(second.Principal);
        Assert.Equal(["Cookies", "Cookies"], _auth.SignOuts);
        var entry = Assert.Single(_audit);
        Assert.Equal("session-ended", entry.Action);
    }

    [Fact]
    public async Task AnEndedSession_NeverReachesTheInnerHandler()
    {
        _policy.Default = new SessionLimits { MaxAge = TimeSpan.FromHours(1) };
        _store.Put(Session("s1", _time.Now.AddHours(-2)));
        using var provider = Host();

        await ValidateAsync(provider, WithSession("s1"));

        Assert.Equal(0, _innerCalls);
    }

    /// <summary>What the redirect that follows reads, so the page it lands on can say why. A code, never words.</summary>
    [Theory]
    [InlineData(SessionEndReason.Idle, "idle")]
    [InlineData(SessionEndReason.MaxAge, "maxage")]
    [InlineData(SessionEndReason.NewerSignIn, "newersignin")]
    [InlineData(SessionEndReason.Ended, "ended")]
    [InlineData(SessionEndReason.SignedOut, "signedout")]
    public async Task TheReason_IsLeftOnTheRequest_AsACode(SessionEndReason reason, string code)
    {
        _store.Put(Session("s1", _time.Now.AddHours(-2)) with { EndedAt = _time.Now.AddMinutes(-1), EndReason = reason });
        using var provider = Host();

        var (_, http) = await ValidateAsync(provider, WithSession("s1"));

        Assert.Equal(code, http.Items[SessionCookieEvents.ReasonItemKey]);
    }

    [Theory]
    [InlineData("/ended", "/", SessionEndReason.Idle, "/ended?reason=idle")]
    [InlineData(null, "/", SessionEndReason.MaxAge, "/?reason=maxage")]
    [InlineData(null, "/home", SessionEndReason.NewerSignIn, "/home?reason=newersignin")]
    [InlineData("/ended?from=a", "/", SessionEndReason.Idle, "/ended?from=a&reason=idle")]
    public void WhereTheBrowserIsSent_IsTheEndedPath_OrHome_WithTheCode(string endedPath, string homePath, SessionEndReason reason, string expected)
    {
        Assert.Equal(expected, SessionCookieEvents.EndedLocation(endedPath, homePath, reason));
    }

    /// <summary>The same rule as the login endpoint's return path: only a path on this site, so the redirect is no open one.</summary>
    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/path")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\t/evil.example")]
    public void AnEndedPath_ThatIsNotLocal_FallsBackToHome(string endedPath)
    {
        Assert.Equal("/?reason=idle", SessionCookieEvents.EndedLocation(endedPath, "/", SessionEndReason.Idle));
    }

    // ---- the team

    [Fact]
    public async Task TheTeam_IsTheOneTheSelectedTeamCookieNames_BecauseTheClaimsDoNotExistYet()
    {
        _store.Put(Session("s1", _time.Now.AddMinutes(-5)));
        using var provider = Host();

        await ValidateAsync(provider, WithSession("s1"), team: "strict");

        Assert.Equal([("s1", "strict")], _policy.Asked);
    }

    // ---- failures

    [Fact]
    public async Task APolicyThatThrows_LetsTheRequestThrough_WhenAllowed()
    {
        _policy.Failure = new InvalidOperationException("down");
        _store.Put(Session("s1", _time.Now.AddMinutes(-5)));
        using var provider = Host();
        var principal = WithSession("s1");

        var (context, _) = await ValidateAsync(provider, principal);

        Assert.Same(principal, context.Principal);
        Assert.Equal(1, _innerCalls);
        Assert.Empty(_auth.SignOuts);
    }

    [Fact]
    public async Task APolicyThatThrows_RefusesTheRequest_WhenSetToDeny_WithoutEndingTheSession()
    {
        _policy.Failure = new InvalidOperationException("down");
        _store.Put(Session("s1", _time.Now.AddMinutes(-5)));
        using var provider = Host(sessions: o => o.OnError = SessionErrorBehavior.Deny);

        var (context, _) = await ValidateAsync(provider, WithSession("s1"));

        Assert.Null(context.Principal);
        Assert.Empty(_store.Ends);
        Assert.Empty(_audit);
    }

    // ---- which scheme

    /// <summary>API keys and MCP authenticate through other schemes, with no cookie and no session.</summary>
    [Fact]
    public async Task AnotherScheme_IsNotTouched()
    {
        _store.Put(Session("s1", _time.Now.AddHours(-2)) with { EndedAt = _time.Now, EndReason = SessionEndReason.Idle });
        using var provider = Host(innerScheme: "Other");
        var principal = WithSession("s1");

        var (context, _) = await ValidateAsync(provider, principal, scheme: "Other");

        Assert.Same(principal, context.Principal);
        Assert.Equal(0, _store.Calls);
        Assert.Equal(1, _innerCalls);
    }

    [Fact]
    public async Task TheCookieScheme_CanBeNamedByTheHost()
    {
        _policy.Default = new SessionLimits { MaxAge = TimeSpan.FromHours(1) };
        _store.Put(Session("s1", _time.Now.AddHours(-2)));
        using var provider = Host(sessions: o => o.CookieScheme = "HostCookies", innerScheme: "HostCookies");

        var (named, _) = await ValidateAsync(provider, WithSession("s1"), scheme: "HostCookies");
        var (default_, _) = await ValidateAsync(provider, WithSession("s1"), scheme: "Cookies");

        Assert.Null(named.Principal);
        Assert.NotNull(default_.Principal);
    }
}
