using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// The endpoint the page reports activity to (Tharga/Team#471), mapped by <c>UseThargaTeamBlazor</c>: someone working inside a Blazor circuit sends no
/// request, so without it an idle timeout would end the session of a person who is busy.
/// </summary>
/// <remarks>
/// <b>It does nothing but say the caller's session was used now</b>, in the team the selected-team cookie names; the store throttles to one write per
/// <see cref="SessionOptions.ActivityInterval"/> per session, so several tabs and instances cost no more than one. It takes nothing from the body and names
/// no session: the session is the one in the caller's cookie. A cookie with no session is not an error (the next ordinary request adopts it), with
/// sessions off it does nothing, and a store that fails is logged, never a server error: the next report catches up.
/// </remarks>
internal static class SessionActivityEndpoint
{
    /// <summary>Where the browser posts; a POST.</summary>
    internal const string Route = "/_tharga/session/activity";

    internal static void Map(WebApplication app) => app.MapPost(Route, HandleAsync);

    // The cancellation token is a parameter, not read from the context, so this binds as a handler whose result is written, not as a bare RequestDelegate.
    internal static async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.User?.Identity?.IsAuthenticated != true) return Results.Unauthorized();

        var evaluator = context.RequestServices.GetService<SessionEvaluator>();
        if (evaluator?.Active != true) return Results.NoContent();

        try
        {
            await evaluator.RecordActivityAsync(context.User, context.Request.Cookies[Constants.SelectedTeamKeyCookie], cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger(typeof(SessionActivityEndpoint)).LogWarning(e, "Session activity could not be recorded.");
        }

        return Results.NoContent();
    }
}
