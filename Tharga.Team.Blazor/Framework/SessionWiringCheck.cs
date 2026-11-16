using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Fails startup when server-side sessions are on and could not take effect (Tharga/Team#471): a policy with no store to keep sessions in, or a cookie
/// scheme whose <c>EventsType</c> replaces the events the session check is chained into.
/// </summary>
/// <remarks>
/// <b>It throws, like <see cref="TeamCacheWiringCheck"/>, and whatever <c>ThrowOnIncompleteTeamService</c> says</b>: it can only fire for a host that
/// registered a policy, so firing means limits the host asked for would quietly never apply, and those limits are the thing the policy is for. With no
/// policy, or with <see cref="SessionOptions.Enabled"/> off, it checks nothing.
/// </remarks>
internal sealed class SessionWiringCheck(
    IServiceProviderIsService registered,
    IOptions<ThargaBlazorOptions> options,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var sessions = options.Value.Sessions;
        if (sessions?.Enabled != true || !registered.IsService(typeof(ISessionPolicy))) return Task.CompletedTask;

        if (!registered.IsService(typeof(IUserSessionStore)))
        {
            throw new InvalidOperationException(
                $"An {nameof(ISessionPolicy)} is registered, but no {nameof(IUserSessionStore)} to keep sessions in, so its limits would never apply. " +
                $"Call AddThargaTeamRepository (Tharga.Team.MongoDB), which registers one, or register your own {nameof(IUserSessionStore)}.");
        }

        var eventsType = cookieOptions.Get(sessions.CookieScheme).EventsType;
        if (eventsType != null)
        {
            throw new InvalidOperationException(
                $"The cookie scheme '{sessions.CookieScheme}' sets {nameof(CookieAuthenticationOptions)}.{nameof(CookieAuthenticationOptions.EventsType)} ({eventsType.Name}), " +
                $"which replaces the events the server-side session check is chained into, so session limits would never apply. Set the handlers on " +
                $"{nameof(CookieAuthenticationOptions)}.{nameof(CookieAuthenticationOptions.Events)} instead, or switch sessions off with SessionOptions.Enabled = false.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
