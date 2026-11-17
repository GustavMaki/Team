using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Maps the platform's AI catalogue as HTTP endpoints: everything <see cref="IPlatformAiCatalogueService"/> does, for staff or a system key.
/// </summary>
public static class PlatformAiCatalogueEndpointRegistration
{
    /// <summary>Default path the endpoints are mapped under.</summary>
    public const string DefaultPath = "/api/support/assistant/catalogue";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Maps the catalogue endpoints under <paramref name="path"/>.</summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Route</term><description>Does</description></listheader>
    /// <item><term><c>GET {path}</c></term><description>The providers (without keys) and the models, with the default.</description></item>
    /// <item><term><c>PUT {path}/provider</c></term><description>Adds a provider or changes one. The key is accepted here and never returned.</description></item>
    /// <item><term><c>DELETE {path}/provider/{id}</c></term><description>Removes a provider no model is reached through.</description></item>
    /// <item><term><c>GET {path}/provider/{id}/models</c></term><description>The models the provider's stored key may use.</description></item>
    /// <item><term><c>PUT {path}/models</c></term><description>Replaces the models and the default.</description></item>
    /// <item><term><c>POST {path}/models/{id}/test</c></term><description>Asks the model one short question.</description></item>
    /// </list>
    /// <para>
    /// <b>No authorization here.</b> The service requires <see cref="SystemSupportScopes.AiCatalogueManage"/> and audits every change, so the
    /// endpoints inherit both; a refusal is a 403. They require the toolkit's API policy. What did not work answers 422 with why in the
    /// body, and nothing in any answer carries a key.
    /// </para>
    /// </remarks>
    public static RouteGroupBuilder MapThargaSupportPlatformAiCatalogue(this IEndpointRouteBuilder endpoints, string path = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(path).RequireAuthorization(ApiKeyConstants.ThargaApiPolicyName);

        // Every handler takes the service, not only the HttpContext, or the result it returns is never written.
        group.MapGet("", (HttpContext context, IPlatformAiCatalogueService service) => RunAsync(async ct
            => Results.Json(await service.GetCatalogueAsync(ct), Json), context));

        group.MapPut("/provider", (HttpContext context, IPlatformAiCatalogueService service) => RunWithBodyAsync<PlatformAiProviderRequest>(context, async (request, ct)
            => Outcome(await service.SetProviderAsync(request, ct))));

        group.MapDelete("/provider/{id}", (HttpContext context, IPlatformAiCatalogueService service, string id) => RunAsync(async ct
            => Outcome(await service.RemoveProviderAsync(id, ct)), context));

        group.MapGet("/provider/{id}/models", (HttpContext context, IPlatformAiCatalogueService service, string id) => RunAsync(async ct =>
        {
            var list = await service.ListProviderModelsAsync(id, ct);
            return Results.Json(list, Json, statusCode: list.Problem == null ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
        }, context));

        group.MapPut("/models", (HttpContext context, IPlatformAiCatalogueService service) => RunWithBodyAsync<PlatformAiModelsRequest>(context, async (request, ct)
            => Outcome(await service.SetModelsAsync(request, ct))));

        group.MapPost("/models/{id}/test", (HttpContext context, IPlatformAiCatalogueService service, string id) => RunAsync(async ct
            => Outcome(await service.TestModelAsync(id, ct)), context));

        return group;
    }

    private static IResult Outcome(SupportAiConnectionResult result)
        => Results.Json(result, Json, statusCode: result.Succeeded ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> RunAsync(Func<CancellationToken, Task<IResult>> operation, HttpContext context)
    {
        try
        {
            return await operation(context.RequestAborted);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }

    private static Task<IResult> RunWithBodyAsync<T>(HttpContext context, Func<T, CancellationToken, Task<IResult>> operation) where T : class
        => RunAsync(async ct =>
        {
            T request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<T>(Json, ct);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // Not the exception's text: it can quote the body, and the body may hold a key.
                return Results.Problem($"The body must be a {typeof(T).Name} in JSON.", statusCode: StatusCodes.Status400BadRequest);
            }

            return request == null ? Results.Problem($"The body must be a {typeof(T).Name} in JSON.", statusCode: StatusCodes.Status400BadRequest) : await operation(request, ct);
        }, context);
}
