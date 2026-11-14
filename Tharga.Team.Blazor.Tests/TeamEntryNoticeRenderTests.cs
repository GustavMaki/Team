using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The explanation for a team the host's guard refused: the reason, and a way to do what the guard asks. It renders nothing when nothing is refused, so
/// it can stay in the layout.
/// </summary>
public class TeamEntryNoticeRenderTests : BunitContext
{
    private readonly Mock<ITeamEntryService> _entry = new();

    public TeamEntryNoticeRenderTests()
    {
        Services.AddSingleton(_entry.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void WithNothingRefused_NothingIsRendered()
    {
        _entry.SetupGet(x => x.Refusal).Returns((TeamEntryRefusal)null);

        var view = Render<TeamEntryNotice>();

        Assert.Empty(view.Markup.Trim());
    }

    [Fact]
    public void ADeny_ShowsTheGuardsReason_AndNoWayOnward()
    {
        _entry.SetupGet(x => x.Refusal).Returns(new TeamEntryRefusal("acme", TeamEntryDecision.Deny("The contract with Acme has ended.")));

        var view = Render<TeamEntryNotice>();

        Assert.Equal("The contract with Acme has ended.", view.Find("[data-team-entry-reason]").TextContent.Trim());
        Assert.Empty(view.FindAll("[data-team-entry-step-up]"));
    }

    [Fact]
    public void ARefusalWithNoReason_SaysSoInTheLibrarysWords()
    {
        _entry.SetupGet(x => x.Refusal).Returns(new TeamEntryRefusal("acme", new TeamEntryDecision { Outcome = TeamEntryOutcome.Deny }));

        var view = Render<TeamEntryNotice>();

        Assert.Equal(TeamEntryText.DefaultReason.Default, view.Find("[data-team-entry-reason]").TextContent.Trim());
    }

    [Fact]
    public void AStepUp_OffersToContinue_AndTheButtonSendsTheBrowserThere()
    {
        var refusal = new TeamEntryRefusal("acme", TeamEntryDecision.StepUp("https://id.example/step-up?return={returnUrl}", "Sign in with BankID to work in Acme."));
        _entry.SetupGet(x => x.Refusal).Returns(refusal);
        var view = Render<TeamEntryNotice>();

        view.Find("[data-team-entry-step-up]").Click();

        Assert.Equal("Sign in with BankID to work in Acme.", view.Find("[data-team-entry-reason]").TextContent.Trim());
        _entry.Verify(x => x.StepUp(refusal), Times.Once);
    }

    [Fact]
    public void WhenTheRefusalChanges_TheNoticeFollows()
    {
        _entry.SetupGet(x => x.Refusal).Returns((TeamEntryRefusal)null);
        var view = Render<TeamEntryNotice>();
        Assert.Empty(view.FindAll("[data-team-entry-notice]"));

        _entry.Raise(x => x.RefusalChanged += null, _entry.Object, new TeamEntryRefusal("acme", TeamEntryDecision.Deny("No.")));

        view.WaitForAssertion(() => Assert.Equal("No.", view.Find("[data-team-entry-reason]").TextContent.Trim()));

        _entry.Raise(x => x.RefusalChanged += null, _entry.Object, (TeamEntryRefusal)null);

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-team-entry-notice]")));
    }

    [Fact]
    public void TheHostCanWordTheHeading()
    {
        _entry.SetupGet(x => x.Refusal).Returns(new TeamEntryRefusal("acme", TeamEntryDecision.Deny("No.")));

        var view = Render<TeamEntryNotice>(p => p.Add(x => x.Title, "Not yet, organisation"));

        Assert.Contains("Not yet, organisation", view.Markup);
    }
}
