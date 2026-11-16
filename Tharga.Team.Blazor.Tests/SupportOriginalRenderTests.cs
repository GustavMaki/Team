using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A received mail's original, kept as it arrived: offered in the transcript only where one exists, saved as an <c>.eml</c> or read as text, and
/// only ever through the service (Tharga/Team#499).
/// </summary>
public class SupportOriginalRenderTests : BunitContext
{
    private const string ModulePath = "./_content/Tharga.Team.Blazor/support-attachments.js";
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";

    private static readonly byte[] Raw = "From: user@example.com\r\nSubject: Export\r\n\r\nIt is empty."u8.ToArray();

    private readonly Mock<ISupportCaseService> _cases = new();

    private static SupportMessage Received(string originalId = "orig-1") => new()
    {
        Sequence = 3,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Body = "It is empty.",
        SentAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
        OriginalId = originalId
    };

    [Fact]
    public void AMessageWithAnOriginal_OffersToSaveItAndToReadIt()
    {
        var view = RenderTranscript(Received());

        Assert.Contains(SupportTranscriptText.DownloadOriginal.Default, view.Find("[data-original-download='3']").TextContent);
        Assert.Contains(SupportTranscriptText.ReadOriginal.Default, view.Find("[data-original-read='3']").TextContent);
    }

    /// <summary>Everything received before originals were kept, and everything written on the site.</summary>
    [Fact]
    public void AMessageWithoutAnOriginal_OffersNeither()
    {
        var view = RenderTranscript(Received(originalId: null));

        Assert.Empty(view.FindAll("[data-original]"));
        Assert.Empty(view.FindAll("[data-original-download]"));
        Assert.Empty(view.FindAll("[data-original-read]"));
    }

    [Fact]
    public void WithoutACase_NothingIsOffered_ThereIsNobodyToAsk()
    {
        var view = RenderTranscript(Received(), caseId: null);

        Assert.Empty(view.FindAll("[data-original]"));
    }

    [Fact]
    public async Task Saving_ReadsThroughTheServiceForThatEntry_AndSavesAnEmlTheBrowserNeverOpens()
    {
        var module = SetUpModule();
        _cases.Setup(x => x.GetMessageOriginalAsync(TeamKey, CaseId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Raw);

        var view = RenderTranscript(Received());
        await view.Find("[data-original-download='3']").ClickAsync(new());

        _cases.Verify(x => x.GetMessageOriginalAsync(TeamKey, CaseId, 3, It.IsAny<CancellationToken>()), Times.Once);
        var saved = Assert.Single(module.Invocations, x => x.Identifier == "save");
        Assert.Equal("mail-3.eml", saved.Arguments[1]);
        Assert.DoesNotContain(module.Invocations, x => x.Identifier == "open");
    }

    [Fact]
    public async Task Reading_OpensADialogWithTheMailAsText()
    {
        _cases.Setup(x => x.GetMessageOriginalAsync(TeamKey, CaseId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Raw);
        var opened = new List<(string Title, Type Type, IDictionary<string, object> Parameters)>();
        var view = RenderTranscript(Received());
        Services.GetRequiredService<DialogService>().OnOpen += (title, type, parameters, _) => opened.Add((title, type, parameters));

        // The dialog stays open until it is closed, so the click is not awaited to its end.
        _ = view.Find("[data-original-read='3']").ClickAsync(new());
        view.WaitForAssertion(() => Assert.Single(opened));

        Assert.Equal(SupportTranscriptText.OriginalTitle.Default, opened[0].Title);
        Assert.Equal(typeof(SupportOriginalDialog), opened[0].Type);
        Assert.Equal(Encoding.UTF8.GetString(Raw), opened[0].Parameters[nameof(SupportOriginalDialog.Text)]);
        Assert.Null(opened[0].Parameters[nameof(SupportOriginalDialog.CutNote)]);
    }

    /// <summary>A mail of many megabytes is never drawn whole; the download has all of it, and the window says so.</summary>
    [Fact]
    public async Task ALongOriginal_IsShownCut_AndTheWindowSaysSo()
    {
        var long_ = new string('x', SupportTranscript.MaxOriginalChars + 500);
        _cases.Setup(x => x.GetMessageOriginalAsync(TeamKey, CaseId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(Encoding.UTF8.GetBytes(long_));
        var opened = new List<IDictionary<string, object>>();
        var view = RenderTranscript(Received());
        Services.GetRequiredService<DialogService>().OnOpen += (_, _, parameters, _) => opened.Add(parameters);

        _ = view.Find("[data-original-read='3']").ClickAsync(new());
        view.WaitForAssertion(() => Assert.Single(opened));

        Assert.Equal(SupportTranscript.MaxOriginalChars, ((string)opened[0][nameof(SupportOriginalDialog.Text)]).Length);
        Assert.Equal(SupportTranscriptText.OriginalCut.Default, opened[0][nameof(SupportOriginalDialog.CutNote)]);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task AnOriginalTheServiceCannotGive_SaysSo_AndOpensNothing()
    {
        _cases.Setup(x => x.GetMessageOriginalAsync(TeamKey, CaseId, 3, It.IsAny<CancellationToken>())).ReturnsAsync((byte[])null);
        var opened = new List<string>();
        var view = RenderTranscript(Received());
        Services.GetRequiredService<DialogService>().OnOpen += (title, _, _, _) => opened.Add(title);

        await view.Find("[data-original-read='3']").ClickAsync(new());

        Assert.Contains(SupportTranscriptText.OriginalNotAvailable.Default, view.Find("[data-original-unavailable='3']").TextContent);
        Assert.Empty(opened);
    }

    [Fact]
    public void TheDialog_ShowsTheTextAsItIs_AndNotesWhenItIsCut()
    {
        Services.AddRadzenComponents();

        var whole = Render<SupportOriginalDialog>(p => p.Add(x => x.Text, "Received: from mx\n  by host"));
        Assert.Equal("Received: from mx\n  by host", whole.Find("[data-original-text]").TextContent);
        Assert.Empty(whole.FindAll("[data-original-cut]"));

        var cut = Render<SupportOriginalDialog>(p => p.Add(x => x.Text, "start").Add(x => x.CutNote, "Only the start is shown."));
        Assert.Equal("Only the start is shown.", cut.Find("[data-original-cut]").TextContent.Trim());
    }

    /// <summary>The text is markup-encoded like any other, so a mail that contains a tag shows it rather than runs it.</summary>
    [Fact]
    public void TheDialog_NeverRunsWhatTheMailContains()
    {
        Services.AddRadzenComponents();

        var view = Render<SupportOriginalDialog>(p => p.Add(x => x.Text, "<script>alert(1)</script><b>bold</b>"));

        Assert.Empty(view.FindAll("script"));
        Assert.Empty(view.FindAll("[data-original-text] b"));
        Assert.Equal("<script>alert(1)</script><b>bold</b>", view.Find("[data-original-text]").TextContent);
    }

    private BunitJSModuleInterop SetUpModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.Setup<bool>("open", _ => true).SetResult(true);
        module.SetupVoid("save", _ => true).SetVoidResult();
        return module;
    }

    private IRenderedComponent<SupportTranscript> RenderTranscript(SupportMessage message, string caseId = CaseId)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddRadzenComponents();
        Services.AddSingleton(_cases.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportTranscript>(p => p
            .Add(x => x.Messages, [message])
            .Add(x => x.CaseAuthorIdentity, "alice")
            .Add(x => x.TeamKey, TeamKey)
            .Add(x => x.CaseId, caseId));
    }
}
