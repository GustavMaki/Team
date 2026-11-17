using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The assistant's usage for staff: a row per team, provider and model for the period with a total, team names where
/// the reader may see them, and nothing to a reader without the scope.
/// </summary>
public class SupportAssistantUsageViewTests : BunitContext
{
    private readonly Mock<ISupportAssistantUsageService> _usage = new();

    private static readonly SupportAssistantUsageTotal[] Rows =
    [
        new() { TeamKey = "team-a", Provider = "openai", Model = "gpt-4.1", Answers = 3, Declines = 1, ToolCalls = 4, InputTokens = 1000, CachedInputTokens = 600, OutputTokens = 200, TotalTokens = 1200 },
        new() { TeamKey = "team-b", Provider = null, Model = null, Answers = 2, ToolCalls = 1, InputTokens = 500, OutputTokens = 100, TotalTokens = 600 }
    ];

    private IRenderedComponent<SupportAssistantUsageView> Render(ITeamDirectoryService directory = null)
    {
        Services.AddSingleton(_usage.Object);
        if (directory != null) Services.AddSingleton(directory);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<SupportAssistantUsageView>();
    }

    private static DateTime FirstOfThisMonth()
    {
        var today = DateTime.UtcNow.Date;
        return new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void ThisMonth_IsShown_ARowPerTeamProviderAndModel_WithATotal()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);

        var view = Render();

        _usage.Verify(x => x.GetUsageAsync(FirstOfThisMonth(), DateTime.UtcNow.Date.AddDays(1), null, default));
        Assert.Equal(2, view.FindAll("[data-usage-row]").Count);
        var total = view.Find("[data-usage-total]");
        Assert.Equal("5", total.QuerySelector("[data-answers]").TextContent.Trim());
        Assert.Equal("1", total.QuerySelector("[data-declines]").TextContent.Trim());
        Assert.Equal(1800.ToString("N0"), total.QuerySelector("[data-total-tokens]").TextContent.Trim());
        Assert.Equal(600.ToString("N0"), total.QuerySelector("[data-cached-tokens]").TextContent.Trim());
    }

    /// <summary>A provider that named neither itself nor its model is still counted, as unknown.</summary>
    [Fact]
    public void AnUnnamedProviderAndModel_AreShownAsUnknown()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);

        var row = Render().FindAll("[data-usage-row]")[1];

        Assert.Equal(SupportAssistantUsageViewText.Unknown.Default, row.QuerySelector("[data-provider]").TextContent.Trim());
        Assert.Equal(SupportAssistantUsageViewText.Unknown.Default, row.QuerySelector("[data-model]").TextContent.Trim());
    }

    [Fact]
    public void ATeam_IsNamed_WhereTheDirectoryCanBeRead()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns("team-a");
        team.SetupGet(x => x.Name).Returns("Acme");
        var directory = new Mock<ITeamDirectoryService>();
        directory.Setup(x => x.GetTeamsAsync()).Returns(new[] { team.Object }.ToAsyncEnumerable());

        var rows = Render(directory.Object).FindAll("[data-usage-row]");

        Assert.Equal("Acme", rows[0].QuerySelector("[data-team]").TextContent.Trim());
        Assert.Equal("team-b", rows[1].QuerySelector("[data-team]").TextContent.Trim());
    }

    /// <summary>Reading usage is one grant and reading every team another; without the second, the key is shown.</summary>
    [Fact]
    public void ADirectoryThatRefuses_LeavesTheTeamKey()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);
        var directory = new Mock<ITeamDirectoryService>();
        directory.Setup(x => x.GetTeamsAsync()).Throws(new UnauthorizedAccessException());

        var rows = Render(directory.Object).FindAll("[data-usage-row]");

        Assert.Equal("team-a", rows[0].QuerySelector("[data-team]").TextContent.Trim());
    }

    [Fact]
    public void APeriodWithNoUsage_SaysSo()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync([]);

        var view = Render();

        Assert.Equal(SupportAssistantUsageViewText.NoUsage.Default, view.Find("[data-no-usage]").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-usage-total]"));
    }

    [Fact]
    public async Task ChangingThePeriod_AsksAgain()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);
        var view = Render();
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var picker = view.FindComponents<RadzenDatePicker<DateTime>>()[0];
        await view.InvokeAsync(() => picker.Instance.Change.InvokeAsync(from));

        _usage.Verify(x => x.GetUsageAsync(from, DateTime.UtcNow.Date.AddDays(1), null, default));
    }

    /// <summary>What the answers cost is shown where they were priced, and a dash where they were not, rather than a cost of zero.</summary>
    [Fact]
    public void ACost_IsShownWhereAnswersWerePriced_AndADashWhereTheyWereNot()
    {
        SupportAssistantUsageTotal[] rows =
        [
            Rows[0] with { Cost = 0.0125m, Currency = "USD" },
            Rows[1]
        ];
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(rows);

        var view = Render();

        var costs = view.FindAll("[data-usage-row] [data-cost]");
        var shown = 0.0125m.ToString("N4");
        Assert.Contains(shown, costs[0].TextContent);
        Assert.Contains("USD", costs[0].TextContent);
        Assert.Equal(SupportAssistantUsageViewText.NoCost.Default, costs[1].TextContent.Trim());
        Assert.Contains(shown, view.Find("[data-usage-total] [data-cost]").TextContent);
    }

    /// <summary>A cost added up across currencies would mean nothing, so the total is left out.</summary>
    [Fact]
    public void RowsInTwoCurrencies_HaveNoTotalCost()
    {
        SupportAssistantUsageTotal[] rows =
        [
            Rows[0] with { Cost = 1m, Currency = "USD" },
            Rows[1] with { Cost = 2m, Currency = "EUR" }
        ];
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(rows);

        var view = Render();

        Assert.Equal(SupportAssistantUsageViewText.NoCost.Default, view.Find("[data-usage-total] [data-cost]").TextContent.Trim());
        Assert.DoesNotContain("Cost:", view.Find("[data-totals-line]").TextContent);
    }

    [Fact]
    public void TheTotalsLine_AddsUpThePeriod()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);

        var line = Render().Find("[data-totals-line]").TextContent;

        // 3 answered + 1 declined + 2 answered; 1200 + 600 tokens; 1500 in, 600 cached, 300 out.
        Assert.Contains(6.ToString("N0"), line);
        Assert.Contains(1800.ToString("N0"), line);
        Assert.Contains(1500.ToString("N0"), line);
        Assert.Contains(300.ToString("N0"), line);
    }

    [Fact]
    public void ClickingAColumn_SortsByIt_AndAgainReversesIt()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ReturnsAsync(Rows);
        var view = Render();
        string[] Teams() => [.. view.FindAll("[data-usage-row] [data-team]").Select(x => x.TextContent.Trim())];

        Assert.Equal(["team-a", "team-b"], Teams());

        view.Find("[data-sort-answers]").Click();
        Assert.Equal(["team-b", "team-a"], Teams());

        view.Find("[data-sort-answers]").Click();
        Assert.Equal(["team-a", "team-b"], Teams());

        view.Find("[data-sort-team]").Click();
        view.Find("[data-sort-team]").Click();
        Assert.Equal(["team-b", "team-a"], Teams());
    }

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo_AndShownNothing()
    {
        _usage.Setup(x => x.GetUsageAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), null, default)).ThrowsAsync(new UnauthorizedAccessException());

        var view = Render();

        Assert.Contains(SupportAssistantUsageViewText.NotPermitted.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-usage-row]"));
        Assert.Empty(view.FindAll("[data-from]"));
    }
}
