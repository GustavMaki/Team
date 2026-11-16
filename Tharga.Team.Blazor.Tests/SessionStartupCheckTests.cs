using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The startup checks for sessions (Tharga/Team#471), beside <c>TeamServiceCompletenessCheck</c>: a policy with no store to keep sessions in, and a cookie scheme whose
/// <c>EventsType</c> would silently replace the events the session check is chained into, both fail startup naming what is wrong, and only when sessions are active.
/// </summary>
/// <remarks>
/// Both are failures a host would otherwise meet as limits that quietly never apply. The checks are found as the hosted services whose type name ends in
/// <c>Check</c>, so whether they live in <c>TeamServiceCompletenessCheck</c> or beside it is the implementation's to decide (assumption).
/// </remarks>
public class SessionStartupCheckTests
{
    private sealed class HostEvents : CookieAuthenticationEvents;

    private static ServiceCollection Host(Action<ServiceCollection> configure = null, bool throwOnIncomplete = true, Action<SessionOptions> sessions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();
        services.AddScoped<IHostEnvironmentAuthenticationStateProvider>(sp => (ServerAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());
        services.AddThargaTeamBlazor(o =>
        {
            o.RegisterTeamService<StubTeamService, StubUserService, StubMember>();
            o.ThrowOnIncompleteTeamService = throwOnIncomplete;
            o.Sessions = Options(sessions);
        });
        configure?.Invoke(services);
        return services;
    }

    private static async Task StartChecksAsync(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        foreach (var check in provider.GetServices<IHostedService>().Where(x => x.GetType().Name.EndsWith("Check", StringComparison.Ordinal)))
        {
            await check.StartAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WithNoPolicy_StartupIsAsBefore_EvenWithEventsTypeSet()
    {
        await StartChecksAsync(Host(s => s.Configure<CookieAuthenticationOptions>("Cookies", o => o.EventsType = typeof(HostEvents))));
    }

    [Fact]
    public async Task APolicyWithAStore_Starts()
    {
        await StartChecksAsync(Host(s =>
        {
            s.AddSingleton<ISessionPolicy>(new TestSessionPolicy());
            s.AddSingleton<IUserSessionStore>(new InMemorySessionStore());
        }));
    }

    [Fact]
    public async Task APolicyWithNoStore_FailsStartup_NamingTheStore()
    {
        var services = Host(s => s.AddSingleton<ISessionPolicy>(new TestSessionPolicy()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartChecksAsync(services));

        Assert.Contains(nameof(IUserSessionStore), ex.Message);
    }

    /// <summary>Limits that do not apply are not a completeness warning a host may switch off: they are the thing the policy is for.</summary>
    [Fact]
    public async Task APolicyWithNoStore_FailsStartup_WhateverThrowOnIncompleteTeamServiceSays()
    {
        var services = Host(s => s.AddSingleton<ISessionPolicy>(new TestSessionPolicy()), throwOnIncomplete: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartChecksAsync(services));
    }

    [Fact]
    public async Task AnEventsTypeOnTheCookieScheme_FailsStartup_NamingIt_WhenSessionsAreActive()
    {
        var services = Host(s =>
        {
            s.AddSingleton<ISessionPolicy>(new TestSessionPolicy());
            s.AddSingleton<IUserSessionStore>(new InMemorySessionStore());
            s.Configure<CookieAuthenticationOptions>("Cookies", o => o.EventsType = typeof(HostEvents));
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => StartChecksAsync(services));

        Assert.Contains("EventsType", ex.Message);
    }

    [Fact]
    public async Task AnEventsType_OnAnotherScheme_IsNotOurBusiness()
    {
        await StartChecksAsync(Host(s =>
        {
            s.AddSingleton<ISessionPolicy>(new TestSessionPolicy());
            s.AddSingleton<IUserSessionStore>(new InMemorySessionStore());
            s.Configure<CookieAuthenticationOptions>("Other", o => o.EventsType = typeof(HostEvents));
        }));
    }

    [Fact]
    public async Task AnEventsType_IsChecked_OnTheSchemeTheHostNamed()
    {
        var services = Host(s =>
        {
            s.AddSingleton<ISessionPolicy>(new TestSessionPolicy());
            s.AddSingleton<IUserSessionStore>(new InMemorySessionStore());
            s.Configure<CookieAuthenticationOptions>("HostCookies", o => o.EventsType = typeof(HostEvents));
        }, sessions: o => o.CookieScheme = "HostCookies");

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartChecksAsync(services));
    }

    [Fact]
    public async Task SwitchedOff_NeitherCheckApplies()
    {
        await StartChecksAsync(Host(s =>
        {
            s.AddSingleton<ISessionPolicy>(new TestSessionPolicy());
            s.Configure<CookieAuthenticationOptions>("Cookies", o => o.EventsType = typeof(HostEvents));
        }, sessions: o => o.Enabled = false));
    }
}
