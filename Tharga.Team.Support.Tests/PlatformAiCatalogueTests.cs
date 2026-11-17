using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The platform's AI catalogue: staff curate providers and models, a key goes in and never comes out, and a team on the platform is
/// answered by the model it chose (Tharga/Team#512).
/// </summary>
public class PlatformAiCatalogueTests
{
    private const string TeamA = "team-a";
    private const string Staff = "staff-subject";
    private const string GoodKey = SupportAiConnectionServiceTests.GoodKey;

    private readonly InMemoryCatalogueStore _catalogue = new();
    private readonly SupportAiConnectionServiceTests.InMemoryConnectionStore _connections = new();
    private readonly SupportAiConnectionServiceTests.FakeFactory _factory = new();
    private readonly EphemeralDataProtectionProvider _keyRing = new();
    private readonly CollectingAuditLogger _audit = new();

    private static PlatformAiProviderRequest Provider(string name = "Neurolito EU", string apiKey = GoodKey, string id = null, string baseAddress = null)
        => new() { Id = id, Name = name, Kind = SupportAiConnectionKind.Neurolito, ApiKey = apiKey, BaseAddress = baseAddress };

    private static PlatformAiModel Model(string id, string providerId, bool offered = true, decimal? price = 3m, string agreement = null)
        => new() { Id = id, ProviderId = providerId, ModelId = SupportAiConnectionServiceTests.Model, DisplayName = id.ToUpperInvariant(), PriceInput = price, PriceOutput = price == null ? null : price * 5, Offered = offered, AgreementUrl = agreement, ProcessedIn = "Sweden" };

    private IPlatformAiCatalogueService Service(bool withScope = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Staff), new(ClaimTypes.Name, "Sam Staff") };
        if (withScope) claims.Add(new Claim(TeamClaimTypes.SystemScope, SystemSupportScopes.AiCatalogueManage));

        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        return new AuditingPlatformAiCatalogueServiceDecorator(
            new AuthorizationPlatformAiCatalogueServiceDecorator(
                new PlatformAiCatalogueService(_catalogue, _keyRing, authorizer, TimeProvider.System, _factory),
                authorizer),
            new CompositeAuditLogger([_audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    private ISupportAiConnectionService Connections()
    {
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Staff), new Claim(ClaimTypes.Name, "Sam Staff"), new Claim(TeamClaimTypes.TeamKey, TeamA)], "test"))));

        return new SupportAiConnectionService(_connections, _keyRing, authorizer, TimeProvider.System, false, _factory, null, _catalogue);
    }

    private StoredChatClientResolver Resolver(Microsoft.Extensions.AI.IChatClient platform = null)
        => new(_connections, _keyRing, new SupportAiClientCache(), NullLogger<StoredChatClientResolver>.Instance, platform, _factory, _catalogue);

    /// <summary>A provider with a model and a default, as staff would set it up.</summary>
    private async Task<string> SetUpAsync(params string[] modelIds)
    {
        var service = Service();
        Assert.True((await service.SetProviderAsync(Provider())).Succeeded);
        var providerId = (await service.GetCatalogueAsync()).Providers.Single().Id;
        var models = (modelIds.Length == 0 ? ["m1"] : modelIds).Select(x => Model(x, providerId)).ToArray();
        var result = await service.SetModelsAsync(new PlatformAiModelsRequest { Models = models, DefaultModelId = models[0].Id });
        Assert.True(result.Succeeded, result.Problem);
        return providerId;
    }

    [Fact]
    public async Task WithoutTheScope_NothingIsReadOrChanged()
    {
        var service = Service(withScope: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetCatalogueAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetProviderAsync(Provider()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RemoveProviderAsync("p"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListProviderModelsAsync("p"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetModelsAsync(new PlatformAiModelsRequest()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.TestModelAsync("m"));
        Assert.Null(await _catalogue.GetAsync());
    }

    [Fact]
    public async Task AKey_IsStoredEncrypted_AndNeverReturned()
    {
        await SetUpAsync();

        var stored = (await _catalogue.GetAsync()).Providers.Single();
        Assert.NotNull(stored.ProtectedApiKey);
        Assert.DoesNotContain(GoodKey, stored.ProtectedApiKey);

        var shown = await Service().GetCatalogueAsync();
        Assert.True(shown.Providers.Single().HasApiKey);
        Assert.DoesNotContain(GoodKey, System.Text.Json.JsonSerializer.Serialize(shown));
        Assert.DoesNotContain(GoodKey, Provider().ToString());
    }

    [Fact]
    public async Task AKeyTheProviderRefuses_IsNotSaved()
    {
        var result = await Service().SetProviderAsync(Provider(apiKey: "wrong-key"));

        Assert.False(result.Succeeded);
        Assert.Equal(SupportAiConnectionServiceTests.FakeFactory.Refused, result.Problem);
        Assert.Null(await _catalogue.GetAsync());
    }

    [Fact]
    public async Task AProviderEchoingTheKey_NeverGetsItBackIntoAProblem()
    {
        var factory = new SupportAiConnectionServiceTests.FakeFactory { EchoKeyInProblem = true };
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Staff), new Claim(TeamClaimTypes.SystemScope, SystemSupportScopes.AiCatalogueManage)], "test"))));
        var service = new PlatformAiCatalogueService(_catalogue, _keyRing, authorizer, TimeProvider.System, factory);

        var result = await service.SetProviderAsync(Provider(apiKey: "secret-key-123"));

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("secret-key-123", result.Problem);
    }

    [Fact]
    public async Task LeavingTheKeyOut_KeepsTheStoredOne_WhileTheAddressIsUnchanged()
    {
        var providerId = await SetUpAsync();
        var before = (await _catalogue.GetAsync()).Providers.Single().ProtectedApiKey;

        var result = await Service().SetProviderAsync(Provider(name: "Renamed", apiKey: null, id: providerId));

        Assert.True(result.Succeeded, result.Problem);
        var stored = (await _catalogue.GetAsync()).Providers.Single();
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal(before, stored.ProtectedApiKey);
    }

    /// <summary>Changing the address and leaving the key out would send the stored key to a server of the editor's choosing.</summary>
    [Fact]
    public async Task AStoredKey_IsNotKeptForANewAddress()
    {
        var providerId = await SetUpAsync();

        var result = await Service().SetProviderAsync(Provider(apiKey: null, id: providerId, baseAddress: "https://elsewhere.example"));

        Assert.False(result.Succeeded);
        Assert.Equal("Enter the key.", result.Problem);
    }

    [Fact]
    public async Task AProviderThatIsNotThere_CannotBeChanged()
    {
        var result = await Service().SetProviderAsync(Provider(id: "nope"));

        Assert.False(result.Succeeded);
        Assert.Equal("There is no such provider.", result.Problem);
    }

    [Fact]
    public async Task ThePlatformsModels_AreSavedWithTheirPricesAndTheDefault()
    {
        await SetUpAsync("m1", "m2");

        var shown = await Service().GetCatalogueAsync();

        Assert.Equal(["m1", "m2"], shown.Models.Select(x => x.Id));
        Assert.Equal("m1", shown.DefaultModelId);
        Assert.Equal(3m, shown.Models[0].PriceInput);
        Assert.Equal("Sam Staff", shown.ChangedBy);
    }

    [Fact]
    public async Task AModel_MustNameAProviderThatExists()
    {
        await SetUpAsync();

        var result = await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", "no-such-provider")], DefaultModelId = "m1" });

        Assert.False(result.Succeeded);
        Assert.Contains("provider that does not exist", result.Problem);
    }

    [Fact]
    public async Task AnIdentifier_CannotBeUsedTwice()
    {
        var providerId = await SetUpAsync();

        var result = await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId), Model("m1", providerId)], DefaultModelId = "m1" });

        Assert.False(result.Succeeded);
        Assert.Contains("used twice", result.Problem);
    }

    [Fact]
    public async Task TheDefault_MustBeAModelThatIsOffered()
    {
        var providerId = await SetUpAsync();

        var hidden = await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId), Model("m2", providerId, offered: false)], DefaultModelId = "m2" });
        var none = await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId)], DefaultModelId = null });

        Assert.Equal("The default must be a model that is offered.", hidden.Problem);
        Assert.Equal("Choose which offered model is the default.", none.Problem);
    }

    [Theory]
    [InlineData("http://example.com/dpa")]
    [InlineData("https://user:pass@example.com/dpa")]
    [InlineData("javascript:alert(1)")]
    public async Task AnAgreementLink_MustBeAnHttpsAddress(string link)
    {
        var providerId = await SetUpAsync();

        var result = await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId, agreement: link)], DefaultModelId = "m1" });

        Assert.False(result.Succeeded);
        Assert.Contains("https", result.Problem);
    }

    [Fact]
    public async Task APrice_CannotBeNegative()
    {
        var providerId = await SetUpAsync();

        var result = await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId, price: -1m)], DefaultModelId = "m1" });

        Assert.False(result.Succeeded);
        Assert.Contains("negative", result.Problem);
    }

    [Fact]
    public async Task AProvider_CannotBeRemovedWhileAModelIsReachedThroughIt()
    {
        var providerId = await SetUpAsync();

        var refused = await Service().RemoveProviderAsync(providerId);
        await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [] });
        var removed = await Service().RemoveProviderAsync(providerId);

        Assert.False(refused.Succeeded);
        Assert.Contains("Remove them first", refused.Problem);
        Assert.True(removed.Succeeded, removed.Problem);
        Assert.Empty((await Service().GetCatalogueAsync()).Providers);
    }

    [Fact]
    public async Task TheModelsOfAProvider_AreListedWithItsStoredKey()
    {
        var providerId = await SetUpAsync();

        var list = await Service().ListProviderModelsAsync(providerId);

        Assert.Null(list.Problem);
        Assert.Equal(GoodKey, _factory.Seen[^1].ApiKey);
    }

    [Fact]
    public async Task ATest_ReachesTheModelThroughItsProvider()
    {
        await SetUpAsync();

        var result = await Service().TestModelAsync("m1");

        Assert.True(result.Succeeded, result.Problem);
        Assert.Equal(SupportAiConnectionServiceTests.FakeFactory.Reply, result.Reply);
    }

    [Fact]
    public async Task EveryChange_IsAudited_AndNoEntryHoldsTheKey()
    {
        await SetUpAsync();
        await Service().TestModelAsync("m1");

        Assert.Contains(_audit.Entries, x => x.Action == "catalogue-provider-set" && x.Success);
        Assert.Contains(_audit.Entries, x => x.Action == "catalogue-models-set" && x.Success);
        Assert.Contains(_audit.Entries, x => x.Action == "catalogue-test");
        Assert.DoesNotContain(_audit.Entries, x => System.Text.Json.JsonSerializer.Serialize(x).Contains(GoodKey));
    }

    // --- teams choosing ---

    [Fact]
    public async Task TheChoices_ListTheOfferedModels_WithTheirProviderAndTheDefault()
    {
        await SetUpAsync("m1", "m2");

        var choices = await Connections().GetChoicesAsync(TeamA);

        Assert.Contains(SupportAiConnectionKind.Platform, choices.Kinds);
        Assert.Equal(["m1", "m2"], choices.PlatformModels.Select(x => x.Id));
        Assert.Equal("Neurolito EU", choices.PlatformModels[0].Provider);
        Assert.True(choices.PlatformModels[0].IsDefault);
        Assert.False(choices.PlatformModels[1].IsDefault);
        Assert.Equal("Sweden", choices.PlatformModels[0].ProcessedIn);
    }

    [Fact]
    public async Task AModelThatIsNotOffered_IsNotAChoice()
    {
        var providerId = await SetUpAsync();
        await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId), Model("m2", providerId, offered: false)], DefaultModelId = "m1" });

        var choices = await Connections().GetChoicesAsync(TeamA);

        Assert.Equal(["m1"], choices.PlatformModels.Select(x => x.Id));
    }

    [Fact]
    public async Task ATeam_CanChooseAnOfferedModel_AndNotOneThatIsNot()
    {
        var providerId = await SetUpAsync("m1", "m2");
        await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId), Model("m2", providerId), Model("m3", providerId, offered: false)], DefaultModelId = "m1" });

        await Connections().UsePlatformModelAsync(TeamA, "m2");
        var refused = await Record.ExceptionAsync(() => Connections().UsePlatformModelAsync(TeamA, "m3"));

        Assert.Equal("m2", (await Connections().GetConnectionAsync(TeamA)).PlatformModelId);
        Assert.IsType<InvalidOperationException>(refused);
    }

    [Fact]
    public async Task ATeam_ThatChoosesNone_IsOnTheDefault()
    {
        await SetUpAsync();

        await Connections().UsePlatformModelAsync(TeamA, "  ");

        var connection = await Connections().GetConnectionAsync(TeamA);
        Assert.Equal(SupportAiConnectionKind.Platform, connection.Kind);
        Assert.Null(connection.PlatformModelId);
    }

    [Fact]
    public async Task ATeam_IsAnsweredByTheModelItChose_ThroughTheProvidersKey()
    {
        var providerId = await SetUpAsync("m1", "m2");
        await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId), Model("m2", providerId) with { ModelId = FakeModel2 }], DefaultModelId = "m1" });
        await Connections().UsePlatformModelAsync(TeamA, "m2");
        var seen = _factory.Seen.Count;

        var client = await Resolver().GetAsync(TeamA);

        Assert.NotNull(client);
        var endpoint = _factory.Seen[seen];
        Assert.Equal(FakeModel2, endpoint.Model);
        Assert.Equal(GoodKey, endpoint.ApiKey);
        Assert.Equal("https://neurolito.com", endpoint.BaseAddress);
    }

    private const string FakeModel2 = "claude-llama3:70b";

    [Fact]
    public async Task ATeam_ThatNeverChose_IsAnsweredByTheDefault()
    {
        var providerId = await SetUpAsync();
        await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId) with { ModelId = FakeModel2 }], DefaultModelId = "m1" });
        var seen = _factory.Seen.Count;

        Assert.NotNull(await Resolver().GetAsync(TeamA));

        Assert.Equal(FakeModel2, _factory.Seen[seen].Model);
    }

    /// <summary>A model staff stopped offering falls back to the default, rather than leaving the team with no assistant.</summary>
    [Fact]
    public async Task AChosenModelThatIsNoLongerOffered_FallsBackToTheDefault()
    {
        var providerId = await SetUpAsync("m1", "m2");
        await Connections().UsePlatformModelAsync(TeamA, "m2");
        await Service().SetModelsAsync(new PlatformAiModelsRequest
        {
            Models = [Model("m1", providerId) with { ModelId = FakeModel2 }, Model("m2", providerId, offered: false)],
            DefaultModelId = "m1"
        });
        var seen = _factory.Seen.Count;

        Assert.NotNull(await Resolver().GetAsync(TeamA));

        Assert.Equal(FakeModel2, _factory.Seen[seen].Model);
    }

    [Fact]
    public async Task WithNoCatalogue_ThePlatformsRegisteredModelAnswersAsBefore()
    {
        var platform = new SupportAiConnectionServiceTests.FakeFactory().Create(new SupportAiEndpoint(SupportAiConnectionKind.Neurolito, "https://x", null, "m"));

        Assert.Same(platform, await Resolver(platform).GetAsync(TeamA));
    }

    [Fact]
    public async Task AChangedPrice_OrDefault_ReachesTheNextAnswerWithoutARestart()
    {
        var providerId = await SetUpAsync("m1", "m2");
        var resolver = Resolver();
        var first = await resolver.GetAsync(TeamA);

        // The timestamps would tie inside one tick; staff changing the default moves the model.
        await Task.Delay(5);
        await Service().SetModelsAsync(new PlatformAiModelsRequest { Models = [Model("m1", providerId), Model("m2", providerId) with { ModelId = FakeModel2 }], DefaultModelId = "m2" });
        var seen = _factory.Seen.Count;
        var second = await resolver.GetAsync(TeamA);

        Assert.NotSame(first, second);
        Assert.Equal(FakeModel2, _factory.Seen[seen].Model);
    }

    // --- what an answer costs ---

    [Fact]
    public async Task APlatformTeam_IsPricedByItsModel_AndATeamOnItsOwnProviderIsNot()
    {
        await SetUpAsync();
        var pricing = new PlatformAiPricing(_connections, _catalogue);

        Assert.Equal(3m, (await pricing.GetAsync(TeamA))?.PriceInput);

        await _connections.SetAsync(new StoredSupportAiConnection { TeamKey = TeamA, Kind = SupportAiConnectionKind.Neurolito, Model = "x", ChangedAt = DateTime.UtcNow });
        Assert.Null(await pricing.GetAsync(TeamA));

        await _connections.SetAsync(new StoredSupportAiConnection { TeamKey = TeamA, Kind = SupportAiConnectionKind.Off, ChangedAt = DateTime.UtcNow });
        Assert.Null(await pricing.GetAsync(TeamA));
    }

    [Fact]
    public async Task WithNoCatalogue_NothingIsPriced()
        => Assert.Null(await new PlatformAiPricing(_connections, _catalogue).GetAsync(TeamA));

    private sealed class InMemoryCatalogueStore : IPlatformAiCatalogueStore
    {
        private StoredPlatformAiCatalogue _kept;

        public Task<StoredPlatformAiCatalogue> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_kept);

        public Task SetAsync(StoredPlatformAiCatalogue catalogue, CancellationToken cancellationToken = default)
        {
            _kept = catalogue;
            return Task.CompletedTask;
        }
    }

    private sealed class CollectingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Log(AuditEntry entry) => Entries.Add(entry);

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
