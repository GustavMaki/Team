using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Service.Audit;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Where a session starts (Tharga/Team#471): chained into the sign-in's <c>OnTokenValidated</c>, which strips any session claim the identity provider sent and has
/// the library stamp its own, so the claim has the library as its provenance. A start that fails never costs the sign-in; the next request adopts. With no policy,
/// nothing is stamped and the sign-in is audited as it always was.
/// </summary>
/// <remarks>
/// The id's randomness and the stored session are held by <c>UserSessionServiceTests</c>; here <see cref="IUserSessionService"/> is a double, so what is pinned is
/// what the sign-in hands it and what it does with the answer.
/// </remarks>
public class SessionStartTests
{
    private const string ValidAzureAdConfig = """
        {
            "AzureAd": {
                "Authority": "https://test.ciamlogin.com/test",
                "ClientId": "test-client-id",
                "TenantId": "test-tenant-id",
                "CallbackPath": "/signin-oidc"
            }
        }
        """;

    private readonly Mock<IUserSessionService> _sessions = new();
    private readonly TestSessionPolicy _policy = new();
    private readonly List<AuditEntry> _audit = [];
    private readonly SessionClock _time = new();

    private ServiceProvider Host(bool withPolicy = true)
    {
        var builder = WebApplication.CreateBuilder();
        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig))) builder.Configuration.AddJsonStream(stream);
        builder.AddThargaAuth();

        var services = builder.Services;
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddSingleton<TimeProvider>(_time);
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
            o.Sessions = Options();
        });

        services.AddSingleton<IUserSessionStore>(new InMemorySessionStore());
        if (withPolicy) services.AddSingleton<ISessionPolicy>(_policy);
        services.AddScoped(_ => _sessions.Object);
        services.AddSingleton(CollectingAuditBackend.Composite(_audit));

        return services.BuildServiceProvider();
    }

    private async Task<TokenValidatedContext> SignInAsync(ServiceProvider provider, ClaimsPrincipal principal)
    {
        var scope = provider.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        var context = new TokenValidatedContext(http, new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler)), options, principal, new AuthenticationProperties());

        await options.Events.OnTokenValidated(context);
        return context;
    }

    private static ClaimsPrincipal Stamped(ClaimsPrincipal from, string id)
    {
        var identity = new ClaimsIdentity(from.Claims.Where(x => x.Type != TeamClaimTypes.SessionId), "Test");
        identity.AddClaim(new Claim(TeamClaimTypes.SessionId, id, ClaimValueTypes.String, "Tharga.Team"));
        return new ClaimsPrincipal(identity);
    }

    // ---- nothing registered

    [Fact]
    public async Task WithNoPolicyRegistered_NothingIsStamped_AndTheSignInIsAuditedAsBefore()
    {
        using var provider = Host(withPolicy: false);
        var principal = WithoutSession();

        var context = await SignInAsync(provider, principal);

        _sessions.VerifyNoOtherCalls();
        Assert.Same(principal, context.Principal);
        Assert.Equal("signin", Assert.Single(_audit).Action);
    }

    // ---- stamping

    [Fact]
    public async Task WithAPolicy_TheSignInCarriesTheSessionClaimTheLibraryStamped()
    {
        _sessions.Setup(x => x.StartAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((ClaimsPrincipal p) => Stamped(p, "library-session-1"));
        using var provider = Host();

        var context = await SignInAsync(provider, WithoutSession());

        var claim = Assert.Single(context.Principal.FindAll(TeamClaimTypes.SessionId));
        Assert.Equal(("library-session-1", "Tharga.Team"), (claim.Value, claim.Issuer));
        _sessions.Verify(x => x.StartAsync(It.IsAny<ClaimsPrincipal>()), Times.Once);
    }

    /// <summary>The provider is not the library: a session claim it sends is never the session, and is gone before the library stamps its own.</summary>
    [Fact]
    public async Task ASessionClaim_TheIdentityProviderSent_IsStrippedBeforeTheStart()
    {
        ClaimsPrincipal given = null;
        _sessions.Setup(x => x.StartAsync(It.IsAny<ClaimsPrincipal>())).Callback((ClaimsPrincipal p) => given = p).ReturnsAsync((ClaimsPrincipal p) => Stamped(p, "library-session-1"));
        using var provider = Host();
        var fromProvider = new Claim(TeamClaimTypes.SessionId, "chosen-by-the-provider", ClaimValueTypes.String, "https://idp.example");

        var context = await SignInAsync(provider, WithoutSession(extra: [fromProvider, new Claim("idp", "entra")]));

        Assert.NotNull(given);
        Assert.Empty(given.FindAll(TeamClaimTypes.SessionId));
        Assert.Equal("entra", given.FindFirst("idp")?.Value);
        Assert.DoesNotContain(context.Principal.FindAll(TeamClaimTypes.SessionId), x => x.Value == "chosen-by-the-provider");
    }

    // ---- a start that fails

    [Fact]
    public async Task AFailedStart_NeverCostsTheSignIn_TheNextRequestAdopts()
    {
        _sessions.Setup(x => x.StartAsync(It.IsAny<ClaimsPrincipal>())).ThrowsAsync(new InvalidOperationException("the session database is down"));
        using var provider = Host();

        var context = await SignInAsync(provider, WithoutSession(extra: new Claim(TeamClaimTypes.SessionId, "chosen-by-the-provider")));

        Assert.True(context.Principal.Identity?.IsAuthenticated);
        Assert.Equal(Subject, context.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Empty(context.Principal.FindAll(TeamClaimTypes.SessionId));
        Assert.Equal("signin", Assert.Single(_audit).Action);
    }

    [Fact]
    public async Task TheSignInIsStillAudited_OnceEach_WhenASessionIsStarted()
    {
        _sessions.Setup(x => x.StartAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((ClaimsPrincipal p) => Stamped(p, "library-session-1"));
        using var provider = Host();

        await SignInAsync(provider, WithoutSession());

        Assert.Equal("signin", Assert.Single(_audit).Action);
    }
}
