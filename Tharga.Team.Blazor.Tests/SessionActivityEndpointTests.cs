using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Framework;
using static Tharga.Team.Blazor.Tests.SessionTestData;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The endpoint the browser reports activity to (Tharga/Team#471): a Blazor user working inside a circuit sends no HTTP request, so idle time is only right if the
/// page says so. Mapped by <c>UseThargaTeamBlazor</c>, authenticated, a POST, and it does nothing but hand the session id, the time, the team and the interval
/// to the store, which throttles. With sessions off it costs nothing, and a failing store never becomes an error page.
/// </summary>
public class SessionActivityEndpointTests
{
    private readonly SessionClock _time = new();
    private readonly InMemorySessionStore _store = new();

    private static RouteEndpoint Endpoint()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.UseThargaTeamBlazor();

        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).OfType<RouteEndpoint>().Single(x => x.RoutePattern.RawText == SessionActivityEndpoint.Route);
    }

    private async Task<DefaultHttpContext> PostAsync(ClaimsPrincipal user, bool withPolicy = true, string team = null)
    {
        var sessions = Options();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<IUserSessionStore>(_store);
        services.AddSingleton(Wrap(sessions));
        services.AddSingleton(new SessionEvaluator(new SessionCache(_time), Wrap(sessions), _time, withPolicy ? new TestSessionPolicy() : null, Sessions(_store, _time)));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), User = user };
        context.Request.Method = HttpMethods.Post;
        if (team != null) context.Request.Headers.Cookie = $"selected_team_id={team}";

        await Endpoint().RequestDelegate(context);
        return context;
    }

    [Fact]
    public void TheEndpoint_IsMappedByUseThargaTeamBlazor_AsAPost_AndOnlyAPost()
    {
        var methods = Endpoint().Metadata.GetMetadata<IHttpMethodMetadata>();

        Assert.Equal([HttpMethods.Post], methods?.HttpMethods);
    }

    [Fact]
    public async Task ASignedInCaller_HandsTheirSessionToTheStore_WithTheTimeTheTeamAndTheInterval()
    {
        var context = await PostAsync(WithSession("s1"), team: "strict");

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Equal([("s1", _time.Now, "strict", TimeSpan.FromMinutes(1))], _store.Activity);
    }

    [Fact]
    public async Task ACallerWhoIsNotSignedIn_IsRefused_AndNothingIsStored()
    {
        var context = await PostAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(0, _store.Calls);
    }

    /// <summary>A cookie from before sessions: the next ordinary request adopts it; the reporter has nothing to record against.</summary>
    [Fact]
    public async Task ASignedInCaller_WithNoSession_RecordsNothing_AndIsNotAnError()
    {
        var context = await PostAsync(WithoutSession());

        Assert.True(context.Response.StatusCode < 400);
        Assert.Equal(0, _store.Calls);
    }

    [Fact]
    public async Task WithSessionsOff_NothingIsStored()
    {
        var context = await PostAsync(WithSession("s1"), withPolicy: false);

        Assert.Equal(0, _store.Calls);
        Assert.True(context.Response.StatusCode is StatusCodes.Status204NoContent or StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task AStoreThatThrows_IsNeverAServerError()
    {
        _store.Failure = new InvalidOperationException("the session database is down");

        var context = await PostAsync(WithSession("s1"));

        Assert.True(context.Response.StatusCode < 500);
    }
}
