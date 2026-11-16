using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Blazor.Features.Authentication;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Chains the server-side session check into a cookie scheme's events (Tharga/Team#471): <c>OnValidatePrincipal</c> on every request, and the
/// redirect that follows a refusal, so the browser is told why.
/// </summary>
/// <remarks>
/// <b>Chained by <c>AddThargaTeamBlazor</c> into the scheme <see cref="SessionOptions.CookieScheme"/> names</b>, after the host's own configuration, so a
/// host that signs in by its own means is covered as well as <c>AddThargaAuth</c>; API keys and MCP authenticate through other schemes and are never
/// touched. With no policy registered the check costs one resolve and the inner handler runs as it always did.
/// <para>
/// <b>The check runs before the host's handler</b>, which a refused principal never reaches: a host's handler is not asked to vouch for someone the
/// library has already refused. An ended session is signed out of the scheme, so the cookie goes with it; a request refused for any other reason (a
/// session that is not there or not the caller's, or a failure with <see cref="SessionErrorBehavior.Deny"/>) only loses its principal, so a database blip
/// does not delete anyone's cookie.
/// </para>
/// </remarks>
internal static class SessionCookieEvents
{
    /// <summary>The <c>HttpContext.Items</c> key the reason code is left under when a request is refused for an ended session, for the redirect that follows to read.</summary>
    internal const string ReasonItemKey = "Tharga.Team.SessionEndedReason";

    /// <summary>The query parameter the reason code travels in.</summary>
    internal const string ReasonParameterName = "reason";

    /// <summary>The reason as the code the browser is given: its name in lower case, never words, so nothing a visitor can read was written by a visitor.</summary>
    internal static string Code(SessionEndReason reason) => reason.ToString().ToLowerInvariant();

    /// <summary>Where a refused browser is sent: <paramref name="endedPath"/> when it is a path on this site, otherwise <paramref name="homePath"/>, with the reason as a code in <c>reason</c>.</summary>
    /// <remarks>The same rule as the login endpoint's return path, so this is no open redirect.</remarks>
    internal static string EndedLocation(string endedPath, string homePath, SessionEndReason reason)
        => QueryHelpers.AddQueryString(ThargaAuthRegistration.IsLocalPath(endedPath) ? endedPath : homePath, ReasonParameterName, Code(reason));

    /// <summary>Where this request's browser should be sent because its session ended, or null when it did not.</summary>
    internal static string EndedLocation(HttpContext context)
    {
        if (context?.Items[ReasonItemKey] is not string code || !Enum.TryParse<SessionEndReason>(code, true, out var reason)) return null;

        var endedPath = context.RequestServices?.GetService<IOptions<ThargaBlazorOptions>>()?.Value.Sessions?.EndedPath;
        return EndedLocation(endedPath, ThargaAuthRegistration.HomePath, reason);
    }

    /// <summary>The session check, then <paramref name="inner"/> unless the check refused the principal.</summary>
    internal static Func<CookieValidatePrincipalContext, Task> Chain(Func<CookieValidatePrincipalContext, Task> inner)
        => async context =>
        {
            if (!await CheckAsync(context)) return;
            if (inner != null) await inner(context);
        };

    /// <summary>The scheme's own sign-in redirect, sent to <see cref="SessionOptions.EndedPath"/> instead when the session ended on this request; for a host whose challenge is the cookie.</summary>
    internal static Func<RedirectContext<CookieAuthenticationOptions>, Task> ChainRedirectToLogin(Func<RedirectContext<CookieAuthenticationOptions>, Task> inner)
        => context =>
        {
            if (EndedLocation(context.HttpContext) is { } location) context.RedirectUri = location;

            return inner?.Invoke(context) ?? Task.CompletedTask;
        };

    /// <returns>False when the principal was refused.</returns>
    private static async Task<bool> CheckAsync(CookieValidatePrincipalContext context)
    {
        var evaluator = context.HttpContext.RequestServices?.GetService<SessionEvaluator>();
        if (evaluator?.Active != true || context.Principal == null) return true;

        // The claims of the selected team are built later in the pipeline, so the team is the cookie's.
        var team = context.Request.Cookies[Constants.SelectedTeamKeyCookie];
        var check = await evaluator.EvaluateAsync(context.Principal, context.Properties?.IssuedUtc, team, context.HttpContext.RequestAborted);

        switch (check.Outcome)
        {
            case SessionCheckOutcome.Adopted:
                context.ReplacePrincipal(check.Principal);
                context.ShouldRenew = true;
                return true;
            case SessionCheckOutcome.Rejected:
                context.RejectPrincipal();
                return false;
            case SessionCheckOutcome.Ended:
                context.RejectPrincipal();
                context.HttpContext.Items[ReasonItemKey] = Code(check.Reason ?? SessionEndReason.Ended);
                await context.HttpContext.SignOutAsync(context.Scheme.Name);
                return false;
            default:
                return true;
        }
    }
}
