using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Where a browser goes when its session ended on this request (Tharga/Team#471): the challenge that follows the refusal is intercepted, so the browser lands on
/// <see cref="SessionOptions.EndedPath"/> with the reason as a code instead of going straight to the identity provider. Both challenges are covered: the
/// OpenID Connect one <c>AddThargaAuth</c> uses, and the cookie's own for a host that signs in by other means.
/// </summary>
/// <remarks>
/// The reason is read from <c>HttpContext.Items</c>, where the cookie check left it (<c>SessionCookieEventsTests</c> pins that); a request with no reason there
/// is challenged exactly as before.
/// </remarks>
public class SessionEndedRedirectTests
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

    private static ServiceProvider Host(string endedPath = null, bool withAuth = false)
    {
        IServiceCollection services;
        if (withAuth)
        {
            var builder = WebApplication.CreateBuilder();
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidAzureAdConfig))) builder.Configuration.AddJsonStream(stream);
            builder.AddThargaAuth();
            services = builder.Services;
        }
        else
        {
            services = new ServiceCollection();
            services.AddLogging();
        }

        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService>();
            o.Sessions = Options(s => s.EndedPath = endedPath);
        });

        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext Request(ServiceProvider provider, SessionEndReason? ended)
    {
        var http = new DefaultHttpContext { RequestServices = provider.CreateScope().ServiceProvider };
        if (ended != null) http.Items[SessionCookieEvents.ReasonItemKey] = SessionCookieEvents.Code(ended.Value);
        return http;
    }

    private static async Task<RedirectContext<CookieAuthenticationOptions>> CookieChallengeAsync(ServiceProvider provider, HttpContext http)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var context = new RedirectContext<CookieAuthenticationOptions>(http, new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)), options, new AuthenticationProperties(), "/Account/Login?ReturnUrl=%2Fwork");

        await options.Events.OnRedirectToLogin(context);
        return context;
    }

    [Fact]
    public async Task TheCookiesChallenge_AfterAnEndedSession_GoesToTheEndedPath_WithTheCode()
    {
        using var provider = Host("/session-ended");
        var http = Request(provider, SessionEndReason.Idle);

        await CookieChallengeAsync(provider, http);

        Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
        Assert.Equal("/session-ended?reason=idle", http.Response.Headers.Location);
    }

    [Fact]
    public async Task TheCookiesChallenge_WithNoEndedSession_IsAsBefore()
    {
        using var provider = Host("/session-ended");
        var http = Request(provider, null);

        await CookieChallengeAsync(provider, http);

        Assert.Equal("/Account/Login?ReturnUrl=%2Fwork", http.Response.Headers.Location);
    }

    [Fact]
    public async Task WithNoEndedPath_TheBrowserGoesHome_WithTheCode()
    {
        using var provider = Host();
        var http = Request(provider, SessionEndReason.MaxAge);

        await CookieChallengeAsync(provider, http);

        Assert.Equal("/?reason=maxage", http.Response.Headers.Location);
    }

    /// <summary>The ended path is the host's setting, but it is still held to the login endpoint's rule: only a path on this site.</summary>
    [Fact]
    public async Task AnEndedPath_ThatIsNotLocal_IsNotFollowed()
    {
        using var provider = Host("https://evil.example/ended");
        var http = Request(provider, SessionEndReason.NewerSignIn);

        await CookieChallengeAsync(provider, http);

        Assert.Equal("/?reason=newersignin", http.Response.Headers.Location);
    }

    [Fact]
    public async Task TheProvidersChallenge_AfterAnEndedSession_GoesToTheEndedPath_AndNotToTheProvider()
    {
        using var provider = Host("/session-ended", withAuth: true);
        var http = Request(provider, SessionEndReason.Ended);
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        var context = new RedirectContext(http, new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler)), options, new AuthenticationProperties());

        await options.Events.OnRedirectToIdentityProvider(context);

        Assert.True(context.Handled);
        Assert.Equal("/session-ended?reason=ended", http.Response.Headers.Location);
    }
}
