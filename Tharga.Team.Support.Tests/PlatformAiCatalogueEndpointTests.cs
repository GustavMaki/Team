using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The platform's AI catalogue over HTTP, through the real service and its authorization: each route does what its method does for a
/// caller holding the catalogue scope, refuses one that does not, and no response carries a key.
/// </summary>
public sealed class PlatformAiCatalogueEndpointTests : IAsyncLifetime
{
    private const string Path = PlatformAiCatalogueEndpointRegistration.DefaultPath;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly SupportAiConnectionServiceTests.FakeFactory _factory = new();
    private readonly InMemoryStore _store = new();
    private WebApplication _app;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddAuthentication(TestCaller.SchemeName).AddScheme<AuthenticationSchemeOptions, TestCaller>(TestCaller.SchemeName, _ => { });
        builder.Services.AddAuthorizationBuilder().AddPolicy(ApiKeyConstants.ThargaApiPolicyName, x => x.RequireAuthenticatedUser());
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ITeamPrincipalAccessor, HttpContextTeamPrincipalAccessor>();
        builder.Services.AddSingleton<TeamAuthorizer>();
        builder.Services.AddSingleton<IPlatformAiCatalogueStore>(_store);
        builder.Services.AddSingleton(Substitute.For<ISupportCaseStore>());
        builder.Services.Configure<AuditOptions>(_ => { });
        builder.Services.AddSingleton<CompositeAuditLogger>();
        builder.Services.AddSingleton<IAuditEntryFactory>(new AuditEntryFactory(null));
        builder.Services.AddThargaSupportCases();
        builder.Services.AddThargaSupportPlatformAiCatalogue();
        builder.Services.AddSingleton<ISupportChatClientFactory>(_factory);
        builder.Services.AddSingleton(Substitute.For<IChatClient>());

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapThargaSupportPlatformAiCatalogue();

        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private HttpClient Caller(bool withScope = true)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestCaller.Header, "staff");
        if (withScope) client.DefaultRequestHeaders.Add(TestCaller.ScopeHeader, SystemSupportScopes.AiCatalogueManage);
        return client;
    }

    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value, Json), System.Text.Encoding.UTF8, "application/json");

    private static PlatformAiProviderRequest Provider(string apiKey = SupportAiConnectionServiceTests.GoodKey)
        => new() { Name = "Neurolito EU", Kind = SupportAiConnectionKind.Neurolito, ApiKey = apiKey };

    [Fact]
    public async Task ACallerWithoutTheScope_IsRefusedEverywhere()
    {
        var caller = Caller(withScope: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await caller.GetAsync(Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsync($"{Path}/provider", Body(Provider()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.DeleteAsync($"{Path}/provider/p")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.GetAsync($"{Path}/provider/p/models")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsync($"{Path}/models", Body(new PlatformAiModelsRequest()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caller.PostAsync($"{Path}/models/m/test", null)).StatusCode);
        Assert.Null(await _store.GetAsync());
    }

    [Fact]
    public async Task AProviderAndItsModels_AreSetThroughTheApi_AndNoAnswerCarriesTheKey()
    {
        var caller = Caller();

        var set = await caller.PutAsync($"{Path}/provider", Body(Provider()));
        var providerId = (await caller.GetFromJsonAsync<PlatformAiCatalogue>(Path, Json)).Providers.Single().Id;
        var models = await caller.PutAsync($"{Path}/models", Body(new PlatformAiModelsRequest
        {
            Models = [new PlatformAiModel { Id = "m1", ProviderId = providerId, ModelId = SupportAiConnectionServiceTests.Model, DisplayName = "Qwen", PriceInput = 3m, Offered = true }],
            DefaultModelId = "m1"
        }));
        var text = await caller.GetStringAsync(Path);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(HttpStatusCode.OK, models.StatusCode);
        Assert.DoesNotContain(SupportAiConnectionServiceTests.GoodKey, text);
        Assert.Contains("\"hasApiKey\":true", text);
        Assert.Contains("\"defaultModelId\":\"m1\"", text);
    }

    [Fact]
    public async Task AKeyTheProviderRefuses_Is422_WithWhy_AndNothingSaved()
    {
        var response = await Caller().PutAsync($"{Path}/provider", Body(Provider(apiKey: "wrong")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(SupportAiConnectionServiceTests.FakeFactory.Refused, await response.Content.ReadAsStringAsync());
        Assert.Null(await _store.GetAsync());
    }

    [Fact]
    public async Task AProviderStillInUse_CannotBeRemoved_AndIsRemovedOnceNoModelUsesIt()
    {
        var caller = Caller();
        await caller.PutAsync($"{Path}/provider", Body(Provider()));
        var providerId = (await caller.GetFromJsonAsync<PlatformAiCatalogue>(Path, Json)).Providers.Single().Id;
        await caller.PutAsync($"{Path}/models", Body(new PlatformAiModelsRequest
        {
            Models = [new PlatformAiModel { Id = "m1", ProviderId = providerId, ModelId = "x", Offered = true }],
            DefaultModelId = "m1"
        }));

        var refused = await caller.DeleteAsync($"{Path}/provider/{providerId}");
        await caller.PutAsync($"{Path}/models", Body(new PlatformAiModelsRequest()));
        var removed = await caller.DeleteAsync($"{Path}/provider/{providerId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
    }

    [Fact]
    public async Task TheModelsOfAProvider_AreListed_AndATestAsksTheModel()
    {
        var caller = Caller();
        await caller.PutAsync($"{Path}/provider", Body(Provider()));
        var providerId = (await caller.GetFromJsonAsync<PlatformAiCatalogue>(Path, Json)).Providers.Single().Id;
        await caller.PutAsync($"{Path}/models", Body(new PlatformAiModelsRequest
        {
            Models = [new PlatformAiModel { Id = "m1", ProviderId = providerId, ModelId = SupportAiConnectionServiceTests.Model, Offered = true }],
            DefaultModelId = "m1"
        }));

        var list = await caller.GetFromJsonAsync<SupportAiModelList>($"{Path}/provider/{providerId}/models", Json);
        var test = await caller.PostAsync($"{Path}/models/m1/test", null);

        Assert.Equal(2, list.Models.Length);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.Contains(SupportAiConnectionServiceTests.FakeFactory.Reply, await test.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ABodyThatIsNotOne_Is400_WithoutQuotingIt()
    {
        var response = await Caller().PutAsync($"{Path}/provider", new StringContent("{ \"apiKey\": \"secret-key-999\" ", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("secret-key-999", await response.Content.ReadAsStringAsync());
    }

    private sealed class InMemoryStore : IPlatformAiCatalogueStore
    {
        private StoredPlatformAiCatalogue _kept;

        public Task<StoredPlatformAiCatalogue> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_kept);

        public Task SetAsync(StoredPlatformAiCatalogue catalogue, CancellationToken cancellationToken = default)
        {
            _kept = catalogue;
            return Task.CompletedTask;
        }
    }

    private sealed class TestCaller(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "test";
        public const string Header = "X-Test-Caller";
        public const string ScopeHeader = "X-Test-Scope";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var caller)) return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, caller.ToString()), new(ClaimTypes.Name, "Sam Staff") };
            if (Request.Headers.TryGetValue(ScopeHeader, out var scope)) claims.Add(new Claim(TeamClaimTypes.SystemScope, scope.ToString()));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName)));
        }
    }
}
