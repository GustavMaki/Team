using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The settings and the registration of server-side sessions (Tharga/Team#471): the defaults the documentation states, the options reaching the Blazor layer
/// from the <c>AddThargaTeam</c> facade, and what <c>AddThargaTeamBlazor</c> registers, which is nothing active until a policy is.
/// </summary>
public class SessionOptionsTests
{
    [Fact]
    public void TheDefaults_AreThoseTheDocumentationStates()
    {
        var options = new SessionOptions();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), options.ActivityInterval);
        Assert.Null(options.EndedPath);
        Assert.True(options.AdoptExistingCookies);
        Assert.Equal(SessionErrorBehavior.Allow, options.OnError);
        Assert.Equal("Cookies", options.CookieScheme);
    }

    [Fact]
    public void TheBlazorOptions_CarryThem_NextToTeamEntry()
    {
        var options = new ThargaBlazorOptions();

        Assert.NotNull(options.Sessions);
        Assert.NotSame(options.TeamEntry, options.Sessions);
    }

    [Fact]
    public void TheFacadesForwarder_PicksThemUp()
    {
        Assert.Contains(nameof(ThargaBlazorOptions.Sessions), ThargaBlazorOptionsForwarder.ForwardableProperties().Select(p => p.Name));

        var from = new ThargaBlazorOptions();
        from.Sessions.CookieScheme = "HostCookies";
        var to = new ThargaBlazorOptions();
        ThargaBlazorOptionsForwarder.Copy(from, to);

        Assert.Equal("HostCookies", to.Sessions.CookieScheme);
    }

    private static ServiceCollection Host(Action<ServiceCollection> configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService, StubMember>();
            o.Sessions = Options();
        });
        configure?.Invoke(services);
        return services;
    }

    [Fact]
    public void WithNoPolicy_TheEvaluatorIsThere_AndNotActive()
    {
        using var scope = Host().BuildServiceProvider().CreateScope();

        Assert.False(scope.ServiceProvider.GetRequiredService<SessionEvaluator>().Active);
    }

    [Fact]
    public void ARegisteredPolicy_MakesTheEvaluatorActive()
    {
        using var scope = Host(s => s.AddSingleton<ISessionPolicy>(new TestSessionPolicy())).BuildServiceProvider().CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<SessionEvaluator>().Active);
    }

    /// <summary>One cache per instance, shared by every request, as <c>TeamEntryCache</c> is: a cache per scope would cache nothing.</summary>
    [Fact]
    public void TheCache_IsOnePerInstance()
    {
        var provider = Host().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<SessionCache>(), provider.GetRequiredService<SessionCache>());
    }

    [Fact]
    public void TheSessionService_ResolvesOnceAStoreIsThere()
    {
        using var scope = Host(s => s.AddSingleton<IUserSessionStore>(new InMemorySessionStore())).BuildServiceProvider().CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserSessionService>());
    }

    [Fact]
    public void AHostsOwnSessionService_IsNotReplaced()
    {
        var own = new Moq.Mock<IUserSessionService>().Object;

        using var scope = Host(s => s.AddScoped(_ => own)).BuildServiceProvider().CreateScope();

        Assert.Same(own, scope.ServiceProvider.GetRequiredService<IUserSessionService>());
    }
}
