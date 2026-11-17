using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The one place the platform's AI catalogue is authorized: every operation requires <see cref="SystemSupportScopes.AiCatalogueManage"/>.
/// </summary>
/// <remarks>Reading is gated as changing is: it shows which providers the platform has accounts with and what each model costs.</remarks>
internal sealed class AuthorizationPlatformAiCatalogueServiceDecorator(
    IPlatformAiCatalogueService inner,
    TeamAuthorizer authorizer) : IPlatformAiCatalogueService
{
    public async Task<PlatformAiCatalogue> GetCatalogueAsync(CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.GetCatalogueAsync(cancellationToken);
    }

    public async Task<SupportAiConnectionResult> SetProviderAsync(PlatformAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.SetProviderAsync(request, cancellationToken);
    }

    public async Task<SupportAiConnectionResult> RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.RemoveProviderAsync(providerId, cancellationToken);
    }

    public async Task<SupportAiModelList> ListProviderModelsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.ListProviderModelsAsync(providerId, cancellationToken);
    }

    public async Task<SupportAiConnectionResult> SetModelsAsync(PlatformAiModelsRequest request, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.SetModelsAsync(request, cancellationToken);
    }

    public async Task<SupportAiConnectionResult> TestModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        await RequireAsync();
        return await inner.TestModelAsync(modelId, cancellationToken);
    }

    private async Task RequireAsync()
    {
        if (!await authorizer.HasSystemScopeAsync(SystemSupportScopes.AiCatalogueManage))
            throw new UnauthorizedAccessException($"Curating the platform's AI catalogue requires the '{SystemSupportScopes.AiCatalogueManage}' system scope.");
    }
}
