using Blazored.LocalStorage;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Choosing what the support assistant answers a team with: the current choice, only what the host offers, loading a
/// provider's models, testing, saving, and a key that is sent and never shown again.
/// </summary>
public class SupportAssistantConnectionViewTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string Key = "nl-team-key-0123456789";
    private const string Model = "claude-qwen3-coder:30b";
    private const string Address = "https://neurolito.com";

    private readonly Mock<ISupportAiConnectionService> _connections = new();

    private static readonly SupportAiConnectionChoices AllChoices = new()
    {
        Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Off],
        ProviderDefaults = [new SupportAiProviderDefault(SupportAiConnectionKind.Neurolito, Address)]
    };

    private IRenderedComponent<SupportAssistantConnectionView> Render(SupportAiConnection connection = null, SupportAiConnectionChoices choices = null, bool withTeam = true)
    {
        _connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ReturnsAsync(choices ?? AllChoices);
        _connections.Setup(x => x.GetConnectionAsync(TeamKey, default)).ReturnsAsync(connection ?? new SupportAiConnection());

        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(withTeam ? team.Object : null);

        Services.AddSingleton(_connections.Object);
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        return Render<SupportAssistantConnectionView>();
    }

    private const string Radios = "button[role=radio]";

    /// <summary>What is checked: the kind of the card, or the model of a platform model's card.</summary>
    private static string Chosen(IRenderedComponent<SupportAssistantConnectionView> view)
    {
        var card = view.FindAll($"{Radios}[aria-checked=true]").Single();
        return card.GetAttribute("data-model") ?? card.GetAttribute("data-kind");
    }

    private static string[] Offered(IRenderedComponent<SupportAssistantConnectionView> view)
        => [.. view.FindAll(Radios).Select(x => x.GetAttribute("data-model") ?? x.GetAttribute("data-kind"))];

    private static Task ChooseAsync(IRenderedComponent<SupportAssistantConnectionView> view, SupportAiConnectionKind kind)
    {
        view.Find($"{Radios}[data-kind={kind}]").Click();
        return Task.CompletedTask;
    }

    private static SupportAiPlatformModelChoice Qwen => new()
    {
        Id = "qwen", DisplayName = "Qwen3 Coder", Provider = "Neurolito EU", ProcessedIn = "Sweden", StoredIn = "Germany",
        AgreementUrl = "https://neurolito.com/dpa", IsDefault = true
    };

    private static SupportAiPlatformModelChoice Llama => new() { Id = "llama", DisplayName = "Llama 3", Provider = "Hetzner", ProcessedIn = "Finland", AgreementUrl = "https://hetzner.example/dpa" };

    private static readonly SupportAiConnectionChoices WithCatalogue = AllChoices with { PlatformModels = [Qwen, Llama] };

    private static SupportAiConnection Neurolito() => new()
    {
        Kind = SupportAiConnectionKind.Neurolito,
        BaseAddress = Address,
        Model = Model,
        HasApiKey = true,
        ChangedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        ChangedBy = "Ada Admin"
    };

    [Fact]
    public void TheCurrentChoice_IsShown_WithWhoMadeIt()
    {
        var view = Render(Neurolito());

        Assert.Equal(nameof(SupportAiConnectionKind.Neurolito), Chosen(view));
        Assert.Equal(Address, view.Find("input[data-address]").GetAttribute("value"));
        Assert.Contains("Ada Admin", view.Find("[data-changed]").TextContent);
        Assert.Equal(SupportAssistantConnectionViewText.ApiKeyStored.Default, view.Find("[data-key-hint]").TextContent.Trim());
    }

    [Fact]
    public void ATeamThatNeverChose_IsOnThePlatformsModel()
    {
        var view = Render();

        Assert.Equal(nameof(SupportAiConnectionKind.Platform), Chosen(view));
        Assert.Empty(view.FindAll("[data-changed]"));
        Assert.Empty(view.FindAll("input[data-key]"));
    }

    [Fact]
    public void OnlyWhatTheHostOffers_IsOffered()
    {
        var view = Render(choices: new SupportAiConnectionChoices { Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Off] });

        Assert.Equal(["Platform", "Off"], Offered(view));
    }

    [Fact]
    public async Task ChoosingAProvider_StartsFromItsAddress_WithNoKey()
    {
        var view = Render();

        await ChooseAsync(view, SupportAiConnectionKind.Neurolito);

        Assert.Equal(Address, view.Find("input[data-address]").GetAttribute("value"));
        Assert.Equal(SupportAssistantConnectionViewText.ApiKeyPlaceholder.Default, view.Find("[data-key-hint]").TextContent.Trim());
    }

    [Fact]
    public void ThePlatformsModel_IsNamed_WhenItsClientSaysWhatItIs()
    {
        var view = Render(choices: AllChoices with { PlatformModel = "ollama, llama3" });

        Assert.Contains("The platform's model (ollama, llama3)", view.Find($"{Radios}[data-kind=Platform]").TextContent);
    }

    [Fact]
    public void ThePlatformsModel_IsNotNamed_WhenItsClientSaysNothing()
    {
        var view = Render();

        Assert.Contains(SupportAssistantConnectionViewText.KindPlatform.Default, view.Find($"{Radios}[data-kind=Platform]").TextContent);
    }

    private static readonly SupportAiConnectionChoices OpenAICompatible = new()
    {
        Kinds = [SupportAiConnectionKind.OpenAI, SupportAiConnectionKind.Ollama, SupportAiConnectionKind.LlamaCpp, SupportAiConnectionKind.Off],
        ProviderDefaults =
        [
            new SupportAiProviderDefault(SupportAiConnectionKind.OpenAI, "https://api.openai.com/v1"),
            new SupportAiProviderDefault(SupportAiConnectionKind.Ollama, null) { RequiresApiKey = false },
            new SupportAiProviderDefault(SupportAiConnectionKind.LlamaCpp, null) { RequiresApiKey = false }
        ]
    };

    [Fact]
    public void OpenAIOllamaAndLlamaCpp_AreNamed()
    {
        var view = Render(choices: OpenAICompatible);

        string Label(SupportAiConnectionKind kind) => view.Find($"{Radios}[data-kind={kind}]").TextContent.Trim();
        Assert.Equal(SupportAssistantConnectionViewText.KindOpenAI.Default, Label(SupportAiConnectionKind.OpenAI));
        Assert.Equal(SupportAssistantConnectionViewText.KindOllama.Default, Label(SupportAiConnectionKind.Ollama));
        Assert.Equal(SupportAssistantConnectionViewText.KindLlamaCpp.Default, Label(SupportAiConnectionKind.LlamaCpp));
    }

    /// <summary>Provider kinds sit in their own frame, the platform's models in another, and off is a card of its own, all in one radiogroup.</summary>
    [Fact]
    public void TheCards_AreRadios_InOneRadiogroup_InTwoFrames()
    {
        var view = Render(choices: WithCatalogue);

        var group = view.Find("[role=radiogroup]");
        Assert.Equal(["qwen", "llama", "Neurolito", "Off"], Offered(view));
        Assert.Equal(4, group.QuerySelectorAll("button[role=radio]").Length);
        Assert.Equal(2, view.FindAll("button[role=radio][data-model]").Count);
        Assert.Single(view.FindAll("[data-frame=platform] button[role=radio][data-model=qwen]"));
        Assert.Single(view.FindAll("[data-frame=own] button[role=radio][data-kind=Neurolito]"));
        Assert.Empty(view.FindAll("[data-frame] button[data-kind=Off]"));
        Assert.All(view.FindAll("button[role=radio]"), x => Assert.Equal("button", x.GetAttribute("type")));
    }

    /// <summary>Exactly one card is checked at any time, and the frame of the chosen one is marked.</summary>
    [Fact]
    public async Task ExactlyOneCard_IsChecked_AndItsFrameIsMarked()
    {
        var view = Render(Neurolito(), WithCatalogue);

        Assert.Equal("Neurolito", Chosen(view));
        Assert.Contains("assistant-frame--on", view.Find("[data-frame=own]").ClassName);
        Assert.DoesNotContain("assistant-frame--on", view.Find("[data-frame=platform]").ClassName);

        await ChooseAsync(view, SupportAiConnectionKind.Off);

        Assert.Equal("Off", Chosen(view));
        Assert.DoesNotContain("assistant-frame--on", view.Find("[data-frame=own]").ClassName);
    }

    /// <summary>The model a team has not chosen is the default one, marked as such, and the choice shows its agreement.</summary>
    [Fact]
    public void ATeamThatNeverChose_IsOnTheDefaultModel_WithItsDetailsAndAgreement()
    {
        var view = Render(choices: WithCatalogue);

        Assert.Equal("qwen", Chosen(view));
        var card = view.Find("button[data-model=qwen]");
        Assert.Contains("Neurolito EU", card.TextContent);
        Assert.Contains("Sweden", card.TextContent);
        Assert.Contains("Germany", card.TextContent);
        Assert.NotEmpty(card.QuerySelectorAll("[data-default]"));
        Assert.Empty(view.Find("button[data-model=llama]").QuerySelectorAll("[data-default]"));
        Assert.Equal("https://neurolito.com/dpa", view.Find("a[data-agreement]").GetAttribute("href"));
    }

    [Fact]
    public void ATeamsOwnChoice_IsTheOneChecked_WhenItIsStillOffered()
    {
        var view = Render(new SupportAiConnection { Kind = SupportAiConnectionKind.Platform, PlatformModelId = "llama" }, WithCatalogue);

        Assert.Equal("llama", Chosen(view));
        Assert.Equal("https://hetzner.example/dpa", view.Find("a[data-agreement]").GetAttribute("href"));
    }

    [Fact]
    public void AChoiceNoLongerOffered_FallsBackToTheDefault()
    {
        var view = Render(new SupportAiConnection { Kind = SupportAiConnectionKind.Platform, PlatformModelId = "gone" }, WithCatalogue);

        Assert.Equal("qwen", Chosen(view));
    }

    [Fact]
    public void ChoosingAPlatformModel_AndSaving_UsesThatModel()
    {
        var view = Render(Neurolito(), WithCatalogue);

        view.Find("button[data-model=llama]").Click();
        Assert.Equal("llama", Chosen(view));
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => _connections.Verify(x => x.UsePlatformModelAsync(TeamKey, "llama", default), Times.Once));
    }

    /// <summary>An agreement that is not an https link is not offered to follow.</summary>
    [Fact]
    public void AnAgreement_ThatIsNotHttps_IsNotLinked()
    {
        var view = Render(choices: AllChoices with { PlatformModels = [Qwen with { AgreementUrl = "javascript:alert(1)" }] });

        Assert.Empty(view.FindAll("a[data-agreement]"));
    }

    [Fact]
    public void WhenThePlatformOffersNoModel_ItSaysSo_AndTheTeamsOwnProvidersRemain()
    {
        var view = Render(choices: OpenAICompatible);

        Assert.Contains(SupportAssistantConnectionViewText.PlatformOffersNone.Default, view.Find("[data-platform-none]").TextContent);
        Assert.Equal(["OpenAI", "Ollama", "LlamaCpp", "Off"], Offered(view));
    }

    [Fact]
    public void AProvidersForm_LinksToTheProvidersSite()
    {
        var view = Render(Neurolito());

        var link = view.Find("a[data-site]");
        Assert.Equal("https://neurolito.com", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
    }

    /// <summary>A busy form cannot be changed: the cards are disabled and a spinner shows.</summary>
    [Fact]
    public void ABusyForm_DisablesTheCards_AndShowsASpinner()
    {
        var gate = new TaskCompletionSource<SupportAiConnectionResult>();
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default)).Returns(gate.Task);
        var view = Render(Neurolito());

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() =>
        {
            Assert.All(view.FindAll("button[role=radio]"), x => Assert.True(x.HasAttribute("disabled")));
            Assert.NotEmpty(view.FindComponents<RadzenProgressBarCircular>());
        });
        gate.SetResult(SupportAiConnectionResult.Success());
        view.WaitForAssertion(() => Assert.All(view.FindAll("button[role=radio]"), x => Assert.False(x.HasAttribute("disabled"))));
    }

    /// <summary>OpenAI is a provider like any other: address, key, models.</summary>
    [Fact]
    public void ChoosingOpenAI_ShowsTheProvidersForm()
    {
        var view = Render(choices: OpenAICompatible);

        Assert.Equal(nameof(SupportAiConnectionKind.OpenAI), view.Find("[data-provider]").GetAttribute("data-provider"));
        Assert.Equal("https://api.openai.com/v1", view.Find("input[data-address]").GetAttribute("value"));
        Assert.Equal(SupportAssistantConnectionViewText.ApiKeyPlaceholder.Default, view.Find("[data-key-hint]").TextContent.Trim());
    }

    /// <summary>A server of the team's own starts from no address, and says the key is only needed if the server asks.</summary>
    [Fact]
    public async Task ChoosingOllama_StartsFromNoAddress_WithTheKeyOptional()
    {
        var view = Render(choices: OpenAICompatible);

        await ChooseAsync(view, SupportAiConnectionKind.Ollama);

        Assert.Equal(nameof(SupportAiConnectionKind.Ollama), view.Find("[data-provider]").GetAttribute("data-provider"));
        Assert.True(string.IsNullOrEmpty(view.Find("input[data-address]").GetAttribute("value")));
        Assert.Equal(SupportAssistantConnectionViewText.ApiKeyOptional.Default, view.Find("[data-key-hint]").TextContent.Trim());
    }

    /// <summary>With both providers offered, each starts from its own address, not the other's.</summary>
    [Fact]
    public async Task ChoosingAnthropic_StartsFromAnthropicsAddress()
    {
        const string anthropicAddress = "https://api.anthropic.com";
        var view = Render(choices: new SupportAiConnectionChoices
        {
            Kinds = [SupportAiConnectionKind.Platform, SupportAiConnectionKind.Neurolito, SupportAiConnectionKind.Anthropic, SupportAiConnectionKind.Off],
            ProviderDefaults = [new SupportAiProviderDefault(SupportAiConnectionKind.Neurolito, Address), new SupportAiProviderDefault(SupportAiConnectionKind.Anthropic, anthropicAddress)]
        });

        await ChooseAsync(view, SupportAiConnectionKind.Anthropic);

        Assert.Equal(anthropicAddress, view.Find("input[data-address]").GetAttribute("value"));
    }

    [Fact]
    public async Task LoadingModels_SendsTheTypedKey_AndOffersWhatCameBack()
    {
        SupportAiProviderRequest sent = null;
        _connections.Setup(x => x.ListModelsAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(new SupportAiModelList { Models = [new SupportAiModel(Model, "Qwen3 Coder"), new SupportAiModel("claude-llama3:70b", "Llama 3")] });
        var view = Render();
        await ChooseAsync(view, SupportAiConnectionKind.Neurolito);
        view.Find("input[data-key]").Change(Key);

        view.Find("[data-load-models]").Click();

        Assert.Equal(SupportAiConnectionKind.Neurolito, sent.Kind);
        Assert.Equal(Key, sent.ApiKey);
        Assert.Equal(Address, sent.BaseAddress);
        view.WaitForAssertion(() => Assert.Equal(Model, view.FindComponent<RadzenDropDown<string>>().Instance.Value));
    }

    [Fact]
    public async Task AProblem_IsShownInWords()
    {
        _connections.Setup(x => x.ListModelsAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .ReturnsAsync(new SupportAiModelList { Problem = "The key was refused." });
        var view = Render();
        await ChooseAsync(view, SupportAiConnectionKind.Neurolito);

        view.Find("[data-load-models]").Click();

        view.WaitForAssertion(() => Assert.Contains("The key was refused.", view.Find("[data-problem]").TextContent));
    }

    [Fact]
    public void ATest_ShowsWhatTheModelSaid()
    {
        _connections.Setup(x => x.TestAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default)).ReturnsAsync(SupportAiConnectionResult.Success("I can answer."));
        var view = Render(Neurolito());

        view.Find("[data-test]").Click();

        view.WaitForAssertion(() => Assert.Contains("I can answer.", view.Find("[data-reply]").TextContent));
    }

    /// <summary>Kept out of the request when left as it is, so the service keeps the stored key; the typed one is never shown again after saving.</summary>
    [Fact]
    public void Saving_KeepsTheStoredKey_WhenNoneIsTyped_AndClearsATypedOne()
    {
        var requests = new List<SupportAiProviderRequest>();
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => requests.Add(request))
            .ReturnsAsync(SupportAiConnectionResult.Success());
        var view = Render(Neurolito());

        view.Find("[data-save]").Click();
        view.WaitForAssertion(() => Assert.Single(requests));
        Assert.Null(requests[0].ApiKey);
        Assert.Equal(Model, requests[0].Model);

        view.Find("input[data-key]").Change(Key);
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.Equal(2, requests.Count));
        Assert.Equal(Key, requests[1].ApiKey);
        view.WaitForAssertion(() => Assert.Equal(SupportAssistantConnectionView.StoredKeyMask, view.FindComponent<RadzenPassword>().Instance.Value));
    }

    /// <summary>A stored key fills the field with a mask, so it does not look empty; the mask itself is never sent.</summary>
    [Fact]
    public void AStoredKey_ShowsAsAMask_ThatIsNeverSent()
    {
        SupportAiProviderRequest sent = null;
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(SupportAiConnectionResult.Success());
        var view = Render(Neurolito());

        Assert.Equal(SupportAssistantConnectionView.StoredKeyMask, view.Find("input[data-key]").GetAttribute("value"));

        view.Find("input[data-key]").Change(SupportAssistantConnectionView.StoredKeyMask);
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Null(sent.ApiKey);
    }

    [Theory]
    [InlineData("••••••••••••" + Key)]
    [InlineData("••••" + Key + "••")]
    public void TypingIntoTheMask_SendsOnlyWhatWasTyped(string typed)
    {
        SupportAiProviderRequest sent = null;
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default))
            .Callback<string, SupportAiProviderRequest, CancellationToken>((_, request, _) => sent = request)
            .ReturnsAsync(SupportAiConnectionResult.Success());
        var view = Render(Neurolito());

        view.Find("input[data-key]").Change(typed);
        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.Equal(Key, sent?.ApiKey));
    }

    /// <summary>A key is kept only for the address it was entered for, so another address starts with the field empty.</summary>
    [Fact]
    public void AnotherAddress_HasNoMask()
    {
        var view = Render(Neurolito());

        view.Find("input[data-address]").Change("https://elsewhere.example/");

        Assert.True(string.IsNullOrEmpty(view.Find("input[data-key]").GetAttribute("value")));
    }

    [Fact]
    public void ARefusedConnection_IsSaidSo_AndNothingIsReloaded()
    {
        _connections.Setup(x => x.ConnectAsync(TeamKey, It.IsAny<SupportAiProviderRequest>(), default)).ReturnsAsync(SupportAiConnectionResult.Failure("The provider does not offer it."));
        var view = Render(Neurolito());

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => Assert.Contains("The provider does not offer it.", view.Find("[data-problem]").TextContent));
        _connections.Verify(x => x.GetConnectionAsync(TeamKey, default), Times.Once);
    }

    [Fact]
    public async Task ChoosingOff_AndSaving_TurnsTheAssistantOff()
    {
        var view = Render(Neurolito());
        await ChooseAsync(view, SupportAiConnectionKind.Off);

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => _connections.Verify(x => x.TurnOffAsync(TeamKey, default), Times.Once));
    }

    [Fact]
    public async Task ChoosingThePlatformsModel_AndSaving_UsesIt()
    {
        var view = Render(Neurolito());
        await ChooseAsync(view, SupportAiConnectionKind.Platform);

        view.Find("[data-save]").Click();

        view.WaitForAssertion(() => _connections.Verify(x => x.UsePlatformModelAsync(TeamKey, default), Times.Once));
    }

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        _connections.Setup(x => x.GetChoicesAsync(TeamKey, default)).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_connections.Object);
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        var teamState = new Mock<ITeamStateService>();
        teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();

        var view = Render<SupportAssistantConnectionView>();

        Assert.Contains(SupportAssistantConnectionViewText.NotPermitted.Default, view.Markup);
    }

    [Fact]
    public void WithNoTeam_ItAsksForOne()
    {
        var view = Render(withTeam: false);

        Assert.Contains(SupportAssistantConnectionViewText.SelectTeam.Default, view.Markup);
    }
}
