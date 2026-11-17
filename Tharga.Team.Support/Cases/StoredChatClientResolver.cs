using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Answers each team with the connection it chose: the platform's model, its own provider, or nothing.
/// </summary>
/// <remarks>
/// <b>No record means the platform's model</b>, so turning per-team connections on changes nothing for a team until
/// somebody chooses. Off and a provider the host did not install give no model, so the team is not configured and
/// its cases go to people.
/// <para>
/// A key that can no longer be decrypted, because the key ring was lost, gives no model and is logged; asking the
/// team to enter it again is <see cref="ISupportAiConnectionService"/>'s to say when somebody opens it.
/// </para>
/// </remarks>
internal sealed class StoredChatClientResolver(
    ISupportAiConnectionStore store,
    IDataProtectionProvider dataProtection,
    SupportAiClientCache cache,
    ILogger<StoredChatClientResolver> logger,
    IChatClient platform = null,
    ISupportChatClientFactory factory = null,
    IPlatformAiCatalogueStore catalogueStore = null) : ISupportChatClientResolver
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(SupportAiConnectionService.ProtectionPurpose);
    private readonly IDataProtector _catalogueProtector = dataProtection.CreateProtector(PlatformAiCatalogueService.ProtectionPurpose);

    public async Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var stored = teamKey == null ? null : await store.GetAsync(teamKey, cancellationToken);

        switch (stored?.Kind ?? SupportAiConnectionKind.Platform)
        {
            case SupportAiConnectionKind.Platform:
                return await PlatformAsync(teamKey, stored, cancellationToken) ?? platform;
            case SupportAiConnectionKind.Off:
                return null;
        }

        var provider = factory?.Providers.FirstOrDefault(x => x.Kind == stored.Kind);
        if (provider == null || (stored.ProtectedApiKey == null && provider.RequiresApiKey)) return null;

        return cache.GetOrCreate(stored, () =>
        {
            try
            {
                var apiKey = stored.ProtectedApiKey == null ? null : _protector.Unprotect(stored.ProtectedApiKey);
                return factory.Create(new SupportAiEndpoint(stored.Kind, stored.BaseAddress, apiKey, stored.Model));
            }
            catch (CryptographicException)
            {
                logger.LogWarning("The stored AI key for team {TeamKey} can no longer be decrypted, so the team has no assistant until the key is entered again.", teamKey);
                return null;
            }
        });
    }

    /// <summary>
    /// The catalogue's model for a team on the platform: the one it chose while that is still offered, otherwise the default.
    /// Null when no catalogue is kept or it offers nothing, so the registered client answers as it always did.
    /// </summary>
    /// <remarks>Read on every answer, so staff changing the default or a price reaches the next answer; the client is cached until the catalogue or the team's choice changes.</remarks>
    private async Task<IChatClient> PlatformAsync(string teamKey, StoredSupportAiConnection connection, CancellationToken cancellationToken)
    {
        if (catalogueStore == null || factory == null || teamKey == null) return null;

        var catalogue = await catalogueStore.GetAsync(cancellationToken);
        if (catalogue == null) return null;

        var model = PlatformAiPricing.ChooseModel(catalogue, connection?.PlatformModelId);
        var provider = model == null ? null : catalogue.Providers.FirstOrDefault(x => x.Id == model.ProviderId);
        if (provider == null) return null;

        var version = new StoredSupportAiConnection
        {
            TeamKey = teamKey,
            Kind = SupportAiConnectionKind.Platform,
            BaseAddress = provider.BaseAddress,
            Model = model.ModelId,
            ProtectedApiKey = provider.ProtectedApiKey,
            ChangedAt = catalogue.ChangedAt
        };

        return cache.GetOrCreate(version, () =>
        {
            try
            {
                var apiKey = provider.ProtectedApiKey == null ? null : _catalogueProtector.Unprotect(provider.ProtectedApiKey);
                return factory.Create(new SupportAiEndpoint(provider.Kind, provider.BaseAddress, apiKey, model.ModelId));
            }
            catch (CryptographicException)
            {
                logger.LogWarning("The platform catalogue's key for provider {Provider} can no longer be decrypted, so the platform model answers nobody until the key is entered again.", provider.Name);
                return null;
            }
        });
    }
}

/// <summary>
/// One chat client per team, kept until the team's connection changes.
/// </summary>
/// <remarks>
/// <b>Why cache at all:</b> a provider client holds an HTTP connection, and building one for every answer would open
/// a connection for every answer. The version is the record's change time with its protected key, so a new
/// connection, model or key gives a new client on the very next answer.
/// <para>
/// A client that is replaced is not disposed: an answer may still be on its way through it. Replacement happens when
/// somebody changes the connection, which is rare, and the old client is collected like any other object.
/// </para>
/// </remarks>
internal sealed class SupportAiClientCache
{
    private readonly ConcurrentDictionary<string, (string Version, IChatClient Client)> _byTeam = new();

    public IChatClient GetOrCreate(StoredSupportAiConnection connection, Func<IChatClient> create)
    {
        var version = $"{connection.ChangedAt.Ticks}|{connection.Kind}|{connection.BaseAddress}|{connection.Model}|{connection.ProtectedApiKey}";

        if (_byTeam.TryGetValue(connection.TeamKey, out var cached) && cached.Version == version) return cached.Client;

        var client = create();
        if (client != null) _byTeam[connection.TeamKey] = (version, client);
        else _byTeam.TryRemove(connection.TeamKey, out _);

        return client;
    }
}
