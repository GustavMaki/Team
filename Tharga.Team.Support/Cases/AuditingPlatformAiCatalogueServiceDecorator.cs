using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Audits every change to the platform's AI catalogue, and every test of a model, under <c>assistant</c>.
/// </summary>
/// <remarks>
/// <b>The key is never recorded</b>: the metadata is the provider kind and host, or the model. Reads and listing
/// models are not audited; a test is, because it spends tokens on the platform's account. Wraps the authorizing
/// decorator, so a refusal is recorded rather than lost.
/// </remarks>
internal sealed class AuditingPlatformAiCatalogueServiceDecorator(
    IPlatformAiCatalogueService inner,
    CompositeAuditLogger auditLogger,
    IAuditEntryFactory auditEntryFactory) : IPlatformAiCatalogueService
{
    private const string Feature = "assistant";

    public Task<PlatformAiCatalogue> GetCatalogueAsync(CancellationToken cancellationToken = default)
        => inner.GetCatalogueAsync(cancellationToken);

    public Task<SupportAiModelList> ListProviderModelsAsync(string providerId, CancellationToken cancellationToken = default)
        => inner.ListProviderModelsAsync(providerId, cancellationToken);

    public Task<SupportAiConnectionResult> SetProviderAsync(PlatformAiProviderRequest request, CancellationToken cancellationToken = default)
        => AuditAsync("catalogue-provider-set", nameof(SetProviderAsync), request?.ApiKey, Meta(request?.Kind.ToString(), request?.BaseAddress), () => inner.SetProviderAsync(request, cancellationToken));

    public Task<SupportAiConnectionResult> RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default)
        => AuditAsync("catalogue-provider-remove", nameof(RemoveProviderAsync), null, Meta(null, null), () => inner.RemoveProviderAsync(providerId, cancellationToken));

    public Task<SupportAiConnectionResult> SetModelsAsync(PlatformAiModelsRequest request, CancellationToken cancellationToken = default)
        => AuditAsync("catalogue-models-set", nameof(SetModelsAsync), null, Meta(null, null, request?.DefaultModelId), () => inner.SetModelsAsync(request, cancellationToken));

    public Task<SupportAiConnectionResult> TestModelAsync(string modelId, CancellationToken cancellationToken = default)
        => AuditAsync("catalogue-test", nameof(TestModelAsync), null, Meta(null, null, modelId), () => inner.TestModelAsync(modelId, cancellationToken));

    private async Task<SupportAiConnectionResult> AuditAsync(string action, string methodName, string secret, Dictionary<string, string> metadata, Func<Task<SupportAiConnectionResult>> operation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            Log(action, methodName, sw.ElapsedMilliseconds, result.Succeeded, result.Problem, metadata);
            return result;
        }
        catch (Exception ex)
        {
            Log(action, methodName, sw.ElapsedMilliseconds, false, SupportAiConnectionService.Hide(ex.Message, secret), metadata);
            throw;
        }
    }

    private void Log(string action, string methodName, long durationMs, bool success, string errorMessage, IReadOnlyDictionary<string, string> metadata)
        => auditLogger.Log(auditEntryFactory.Create(Feature, action, methodName, durationMs, success, errorMessage, null, metadata));

    private static Dictionary<string, string> Meta(string kind, string baseAddress, string model = null)
    {
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(kind)) metadata[SupportAuditMetadataKeys.AssistantConnection] = kind;
        if (!string.IsNullOrWhiteSpace(model)) metadata[SupportAuditMetadataKeys.AssistantModel] = model.Trim();
        if (Uri.TryCreate(baseAddress?.Trim(), UriKind.Absolute, out var uri)) metadata[SupportAuditMetadataKeys.AssistantHost] = uri.Host;

        return metadata;
    }
}
