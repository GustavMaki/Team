using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A support case shows what its staff reader already holds on the team and offers a request for more, and shows the team's managers the requests made from
/// it so they can decide them where they read the case (Tharga/Team#379).
/// </summary>
public class SupportCaseAccessRenderTests : BunitContext
{
    private const string TeamKey = "acme";
    private const string CaseId = "case-1";

    private readonly Mock<ITeamDirectoryService> _directory = new();
    private readonly Mock<ITeamAccessRequestService> _requests = new();
    private readonly Mock<ITeamManagementService> _management = new();

    // --- what the staff reader holds, and whether they are offered a request ---

    [Fact]
    public void WithNoAccess_TheReaderIsOfferedARequest_AndToldNothingAboutAccess()
    {
        var view = RenderStaff(access: null);

        Assert.Contains(TeamAccessRequestText.RequestAccess.Default, view.Markup);
        Assert.Empty(view.FindAll("[data-access-level]"));
    }

    [Fact]
    public void WithConsentAtViewer_TheReaderIsToldTheLevel_AndMayStillAskForMore()
    {
        var view = RenderStaff(new CallerTeamAccess(AccessLevel.Viewer, IsMember: false));

        Assert.Equal("Viewer", view.Find("[data-access-level]").GetAttribute("data-access-level"));
        Assert.Contains("through the team's consent", view.Find("[data-access-level]").TextContent);
        Assert.Contains(TeamAccessRequestText.RequestAccess.Default, view.Markup);
    }

    /// <summary>The highest level that can be asked for is held, so there is nothing to ask for: the level is shown instead.</summary>
    [Fact]
    public void WithConsentAtAdministrator_TheReaderIsToldTheLevel_AndNoRequestIsOffered()
    {
        var view = RenderStaff(new CallerTeamAccess(AccessLevel.Administrator, IsMember: false));

        Assert.Equal("Administrator", view.Find("[data-access-level]").GetAttribute("data-access-level"));
        Assert.DoesNotContain(TeamAccessRequestText.RequestAccess.Default, view.Markup);
    }

    [Fact]
    public void AMember_IsToldTheirLevel_AndNoRequestIsOffered()
    {
        var view = RenderStaff(new CallerTeamAccess(AccessLevel.User, IsMember: true));

        Assert.Contains("as a member", view.Find("[data-access-level]").TextContent);
        Assert.DoesNotContain(TeamAccessRequestText.RequestAccess.Default, view.Markup);
    }

    [Fact]
    public void WithoutAConsentRole_TheReaderIsOfferedNothingToAskWith()
    {
        var view = RenderStaff(access: null, roles: ["Other"]);

        Assert.DoesNotContain(TeamAccessRequestText.RequestAccess.Default, view.Markup);
    }

    [Fact]
    public void WithNoTeamServices_NothingIsRendered_AndNothingIsRequired()
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();

        var view = Render<SupportCaseTeamAccess>(p => p.Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId));

        Assert.Empty(view.FindAll("[data-case-access]"));
    }

    // --- the request is made from the case ---

    [Fact]
    public void TheRequestDialog_IsOpenedForTheCase_AndTheReasonStartsFromItsSubject()
    {
        var opened = new List<IDictionary<string, object>>();
        var view = RenderStaff(access: null, subject: "Export is empty");
        Services.GetRequiredService<DialogService>().OnOpen += (_, _, parameters, _) => opened.Add(parameters);

        // The dialog stays open until it is closed, so the click is not awaited to its end.
        _ = view.Find("button").ClickAsync(new());
        view.WaitForAssertion(() => Assert.Single(opened));

        Assert.Equal(TeamKey, opened[0][nameof(TeamAccessRequestDialog.TeamKey)]);
        Assert.Equal(CaseId, opened[0][nameof(TeamAccessRequestDialog.SupportCaseId)]);
        Assert.Equal("Support case: Export is empty", opened[0][nameof(TeamAccessRequestDialog.InitialMessage)]);
    }

    [Fact]
    public void ALongSubject_IsCut_SoTheReasonStaysWithinWhatARequestMayCarry()
    {
        var opened = new List<IDictionary<string, object>>();
        var view = RenderStaff(access: null, subject: new string('x', 5000));
        Services.GetRequiredService<DialogService>().OnOpen += (_, _, parameters, _) => opened.Add(parameters);

        _ = view.Find("button").ClickAsync(new());
        view.WaitForAssertion(() => Assert.Single(opened));

        Assert.True(((string)opened[0][nameof(TeamAccessRequestDialog.InitialMessage)]).Length < TeamAccessRequestRules.MaxMessageLength);
    }

    [Fact]
    public async Task TheDialog_SendsTheRequest_WithTheCase_AndThePrefilledReason()
    {
        SetUpCommon(roles: ["Support"]);
        _requests.Setup(x => x.RequestTeamAccessAsync(TeamKey, It.IsAny<AccessLevel>(), It.IsAny<TimeSpan?>(), "Support case: Export", CaseId))
            .ReturnsAsync(new TeamAccessRequest { Id = "r1", RequesterKey = "staff", AccessLevel = AccessLevel.Viewer, RequestedAt = DateTime.UtcNow, SupportCaseId = CaseId });
        var view = Render<TeamAccessRequestDialog>(p => p
            .Add(x => x.TeamKey, TeamKey)
            .Add(x => x.SupportCaseId, CaseId)
            .Add(x => x.InitialMessage, "Support case: Export"));

        await view.FindAll("button").First(x => x.TextContent.Contains(TeamAccessRequestText.Send.Default)).ClickAsync(new());

        _requests.Verify(x => x.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, It.IsAny<TimeSpan?>(), "Support case: Export", CaseId), Times.Once);
    }

    // --- the managers decide where they read the case ---

    [Fact]
    public void TheManagers_SeeTheRequestsMadeFromThisCase_AndNoOthers()
    {
        var view = RenderRequests(Pending("r1", CaseId), Pending("r2", "another-case"), Pending("r3", null), Pending("r4", CaseId, TeamAccessRequestStatus.Approved));

        var shown = view.FindAll("[data-case-access-requests] button").Select(x => x.TextContent.Trim()).ToArray();

        Assert.Equal(2, shown.Length);
        Assert.Contains(TeamAccessRequestText.Approve.Default, shown[0]);
        Assert.Contains(TeamAccessRequestText.Deny.Default, shown[1]);
        Assert.Contains("Sam Staff", view.Markup);
    }

    [Fact]
    public async Task Denying_IsTheTeamPagesOwnAction_ForThatRequest()
    {
        var view = RenderRequests(Pending("r1", CaseId));

        await view.FindAll("[data-case-access-requests] button").First(x => x.TextContent.Contains(TeamAccessRequestText.Deny.Default)).ClickAsync(new());

        _management.Verify(x => x.DenyTeamAccessRequestAsync(TeamKey, "r1"), Times.Once);
    }

    [Fact]
    public void ACaseWithNoRequestsMadeFromIt_ShowsNothing()
    {
        var view = RenderRequests(Pending("r2", "another-case"));

        Assert.Empty(view.FindAll("[data-case-access-requests] button"));
    }

    /// <summary>The case is read by people who cannot decide; they are shown nothing rather than an error.</summary>
    [Fact]
    public void AReaderWhoMayNotDecide_IsShownNothing_NotAnError()
    {
        SetUpCommon();
        _management.Setup(x => x.GetTeamByKeyAsync(TeamKey)).ReturnsAsync(Team());
        _management.Setup(x => x.GetAccessRequestsAsync(TeamKey)).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_management.Object);

        var view = Render<SupportCaseAccessRequests>(p => p.Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId));

        Assert.Empty(view.FindAll("[data-case-access-requests] button"));
    }

    [Fact]
    public void AReaderWhoMayNotReadTheTeam_IsShownNothing()
    {
        SetUpCommon();
        _management.Setup(x => x.GetTeamByKeyAsync(TeamKey)).ThrowsAsync(new UnauthorizedAccessException());
        Services.AddSingleton(_management.Object);

        var view = Render<SupportCaseAccessRequests>(p => p.Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId));

        Assert.Empty(view.FindAll("[data-case-access-requests]"));
    }

    [Fact]
    public void WithNoTeamService_NothingIsRendered()
    {
        SetUpCommon();

        var view = Render<SupportCaseAccessRequests>(p => p.Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId));

        Assert.Empty(view.FindAll("[data-case-access-requests]"));
    }

    private static TeamAccessRequest Pending(string id, string caseId, TeamAccessRequestStatus status = TeamAccessRequestStatus.Pending) => new()
    {
        Id = id,
        RequesterKey = "staff-1",
        RequesterName = "Sam Staff",
        AccessLevel = AccessLevel.Viewer,
        Duration = TimeSpan.FromHours(4),
        RequestedAt = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc),
        Status = status,
        SupportCaseId = caseId
    };

    private static ITeam Team()
    {
        var team = new Mock<ITeam>();
        team.SetupGet(x => x.Key).Returns(TeamKey);
        team.SetupGet(x => x.Name).Returns("Acme");

        return team.Object;
    }

    private IRenderedComponent<SupportCaseAccessRequests> RenderRequests(params TeamAccessRequest[] stored)
    {
        SetUpCommon();
        _management.Setup(x => x.GetTeamByKeyAsync(TeamKey)).ReturnsAsync(Team());
        _management.Setup(x => x.GetAccessRequestsAsync(TeamKey)).ReturnsAsync(stored);
        Services.AddSingleton(_management.Object);

        return Render<SupportCaseAccessRequests>(p => p.Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId));
    }

    private IRenderedComponent<SupportCaseTeamAccess> RenderStaff(CallerTeamAccess access, string[] roles = null, string subject = "Export is empty")
    {
        SetUpCommon(roles ?? ["Support"]);
        _directory.Setup(x => x.GetMyAccessAsync(TeamKey)).ReturnsAsync(access);
        _directory.Setup(x => x.GetTeamsAsync()).Returns(Teams(access is { IsMember: true } ? [Team()] : []));
        _requests.Setup(x => x.GetMyAccessRequestsAsync()).ReturnsAsync([]);
        Services.AddSingleton(_directory.Object);
        Services.AddSingleton(_requests.Object);

        return Render<SupportCaseTeamAccess>(p => p.Add(x => x.TeamKey, TeamKey).Add(x => x.CaseId, CaseId).Add(x => x.Subject, subject));
    }

    private void SetUpCommon(string[] roles = null)
    {
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        Services.AddSingleton(Options.Create(new ThargaBlazorOptions { Consent = new ConsentOptions { Roles = ["Support"] } }));
        Services.AddSingleton(_requests.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var authorization = this.AddAuthorization();
        authorization.SetAuthorized("staff");
        authorization.SetRoles(roles ?? ["Support"]);
    }

    private static async IAsyncEnumerable<ITeam> Teams(params ITeam[] teams)
    {
        foreach (var team in teams) yield return team;

        await Task.CompletedTask;
    }
}
