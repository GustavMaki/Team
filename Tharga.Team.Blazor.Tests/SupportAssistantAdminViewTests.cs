using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The staff page around the assistant: what each team used, and the platform's AI catalogue, the second only for a caller
/// holding the catalogue scope and only where it is registered (Tharga/Team#512).
/// </summary>
public class SupportAssistantAdminViewTests : BunitContext
{
    private readonly Mock<IPlatformAiCatalogueService> _catalogue = new();

    private IRenderedComponent<SupportAssistantAdminView> RenderAdmin(bool registered = true)
    {
        var usage = new Mock<ISupportAssistantUsageService>();
        usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync([]);
        Services.AddSingleton(usage.Object);
        if (registered) Services.AddSingleton(_catalogue.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportAssistantAdminView>();
    }

    private static PlatformAiCatalogue Kept() => new()
    {
        Providers = [new PlatformAiProvider { Id = "p1", Name = "Neurolito EU", Kind = SupportAiConnectionKind.Neurolito, BaseAddress = "https://neurolito.com", HasApiKey = true }],
        Models = [new PlatformAiModel { Id = "m1", ProviderId = "p1", ModelId = "qwen", DisplayName = "Qwen", ProcessedIn = "Sweden", PriceInput = 3m, PriceOutput = 15m, Currency = "USD", Offered = true }],
        DefaultModelId = "m1",
        ChangedAt = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc),
        ChangedBy = "Sam Staff"
    };

    [Fact]
    public void ACallerWithTheScope_SeesBothTabs()
    {
        _catalogue.Setup(x => x.GetCatalogueAsync(default)).ReturnsAsync(Kept());

        var view = RenderAdmin();

        Assert.Contains(SupportAssistantAdminViewText.UsageTab.Default, view.Markup);
        Assert.Contains(SupportAssistantAdminViewText.ConfigurationTab.Default, view.Markup);
    }

    [Fact]
    public void ACallerWithoutTheScope_SeesNoConfigurationTab()
    {
        _catalogue.Setup(x => x.GetCatalogueAsync(default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = RenderAdmin();

        Assert.Contains(SupportAssistantAdminViewText.UsageTab.Default, view.Markup);
        Assert.DoesNotContain(SupportAssistantAdminViewText.ConfigurationTab.Default, view.Markup);
    }

    [Fact]
    public void WhereTheCatalogueIsNotRegistered_OnlyTheUsageIsShown()
    {
        var view = RenderAdmin(registered: false);

        Assert.Contains(SupportAssistantAdminViewText.UsageTab.Default, view.Markup);
        Assert.DoesNotContain(SupportAssistantAdminViewText.ConfigurationTab.Default, view.Markup);
    }

    [Fact]
    public void TheCatalogueView_ListsProvidersAndModels_WithoutAnyKey()
    {
        _catalogue.Setup(x => x.GetCatalogueAsync(default)).ReturnsAsync(Kept());
        Services.AddSingleton(_catalogue.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<PlatformAiCatalogueView>();

        Assert.Single(view.FindAll("[data-provider-row]"));
        Assert.Contains("Neurolito EU", view.Find("[data-provider-row]").TextContent);
        Assert.Contains(PlatformAiCatalogueViewText.HasKey.Default, view.Find("[data-provider-row]").TextContent);
        var model = view.Find("[data-model-row]");
        Assert.Contains("Qwen", model.TextContent);
        Assert.Contains(PlatformAiCatalogueViewText.Default.Default, model.TextContent);
        Assert.Contains("3", model.TextContent);
        Assert.Contains("USD", model.TextContent);
    }

    [Fact]
    public void TheCatalogueView_SaysSo_WhenTheCallerMayNotCurate()
    {
        _catalogue.Setup(x => x.GetCatalogueAsync(default)).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_catalogue.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<PlatformAiCatalogueView>();

        Assert.Contains(PlatformAiCatalogueViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-provider-row]"));
    }

    [Fact]
    public void AnEmptyCatalogue_SaysThereIsNoProviderYet()
    {
        _catalogue.Setup(x => x.GetCatalogueAsync(default)).ReturnsAsync(new PlatformAiCatalogue());
        Services.AddSingleton(_catalogue.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var view = Render<PlatformAiCatalogueView>();

        Assert.NotNull(view.Find("[data-no-providers]"));
        Assert.NotNull(view.Find("[data-no-models]"));
    }
}
