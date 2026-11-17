namespace Tharga.Team.Support.Cases;

/// <summary>
/// What the platform's model for a team costs, from the catalogue: the model the team chose while it is still offered, otherwise the default.
/// </summary>
/// <remarks>Null for a team that is off or on its own provider, whose answers are not the platform's, and where no catalogue offers a model.</remarks>
internal sealed class PlatformAiPricing(ISupportAiConnectionStore connections, IPlatformAiCatalogueStore catalogue) : ISupportAssistantPricing
{
    public async Task<PlatformAiModel> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (teamKey == null) return null;

        var stored = await connections.GetAsync(teamKey, cancellationToken);
        if (stored != null && stored.Kind != SupportAiConnectionKind.Platform) return null;

        var kept = await catalogue.GetAsync(cancellationToken);
        return kept == null ? null : ChooseModel(kept, stored?.PlatformModelId);
    }

    /// <summary>The team's chosen model while offered, otherwise the default while offered; null when neither is.</summary>
    internal static PlatformAiModel ChooseModel(StoredPlatformAiCatalogue catalogue, string chosenId)
    {
        var offered = catalogue.Models.Where(x => x.Offered).ToArray();
        return offered.FirstOrDefault(x => chosenId != null && x.Id == chosenId) ?? offered.FirstOrDefault(x => x.Id == catalogue.DefaultModelId);
    }
}
