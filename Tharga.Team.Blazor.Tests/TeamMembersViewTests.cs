using System.Security.Claims;
using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// One team's members on a page of their own (Tharga/Team#514): <see cref="TeamMembersView{TMember}"/> alone, the same view inside
/// <see cref="TeamComponent{TMember}"/>, the switch to all teams, and <see cref="CreateTeamButton"/> on its own.
/// </summary>
public class TeamMembersViewTests : BunitContext
{
    private const string Acme = "acme";
    private const string Globex = "globex";
    private const string Ada = "u1";
    private const string Bob = "u2";

    private sealed record Member : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; } = MembershipState.Member;
        public AccessLevel? AccessLevel { get; init; }
        public string[] TenantRoles { get; init; } = [];
        public string[] ScopeOverrides { get; init; } = [];
    }

    private sealed record Team : ITeam<Member>
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Icon { get; init; }
        public Member[] Members { get; init; } = [];
    }

    private sealed class FakeAuthState(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(principal));
    }

    private static async IAsyncEnumerable<T> Stream<T>(IEnumerable<T> items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private static Team AcmeTeam() => new()
    {
        Key = Acme, Name = "Acme",
        Members =
        [
            new Member { Key = Ada, AccessLevel = AccessLevel.Owner },
            new Member { Key = Bob, AccessLevel = AccessLevel.User }
        ]
    };

    private static Team GlobexTeam() => new() { Key = Globex, Name = "Globex", Members = [new Member { Key = Ada, AccessLevel = AccessLevel.Owner }] };

    private readonly Mock<ITeamLifecycleService> _lifecycle = new();
    private readonly Mock<ITeamDirectoryService> _directory = new();

    private void Setup(Team[] teams, string selected = Acme, bool allowCreation = true, string createPath = null)
    {
        _directory.Setup(x => x.GetTeamsAsync<Member>()).Returns(() => Stream<ITeam<Member>>(teams));

        var selectedTeam = selected == null ? null : teams.First(x => x.Key == selected);
        var state = new Mock<ITeamStateService>();
        state.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(selectedTeam);

        var users = new[]
        {
            Mock.Of<IUser>(u => u.Key == Ada && u.EMail == "ada@acme.test" && u.Name == "Ada"),
            Mock.Of<IUser>(u => u.Key == Bob && u.EMail == "bob@acme.test" && u.Name == "Bob")
        };
        var userService = new Mock<IUserService>();
        userService.Setup(x => x.GetCurrentUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(users[0]);
        userService.Setup(x => x.GetTeamMemberUsersAsync()).ReturnsAsync(users);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Ada),
            new(TeamClaimTypes.Scope, TeamScopes.Manage),
            new(TeamClaimTypes.Scope, TeamScopes.MemberManage)
        };

        Services.AddSingleton(_directory.Object);
        Services.AddSingleton(_lifecycle.Object);
        Services.AddSingleton(Mock.Of<ITeamOversightService>());
        var management = new Mock<ITeamManagementService>();
        management.Setup(x => x.GetAccessRequestsAsync(It.IsAny<string>())).ReturnsAsync((IReadOnlyList<TeamAccessRequest>)[]);
        Services.AddSingleton(management.Object);
        Services.AddSingleton(Mock.Of<ITeamAccessRequestService>());
        Services.AddSingleton(Mock.Of<ITeamInvitationService>());
        Services.AddSingleton(state.Object);
        Services.AddSingleton(userService.Object);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthState(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))));
        Services.AddSingleton(Options.Create(new ThargaBlazorOptions { AllowTeamCreation = allowCreation, CreateTeamPath = createPath }));
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddLogging();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));
    }

    private static string[] Rows(IRenderedComponent<TeamMembersView<Member>> view)
        => [.. view.FindAll("tbody tr").Select(x => x.TextContent)];

    [Fact]
    public void AlonePage_ShowsTheSelectedTeamsMembers_AndNothingOfTheTeamList()
    {
        Setup([AcmeTeam(), GlobexTeam()]);

        var view = Render<TeamMembersView<Member>>();

        view.WaitForAssertion(() => Assert.Equal(2, Rows(view).Length));
        Assert.Contains("ada@acme.test", Rows(view)[0] + Rows(view)[1]);
        Assert.Contains("bob@acme.test", Rows(view)[0] + Rows(view)[1]);
        Assert.Empty(view.FindAll("[name=showAllTeams]"));
        Assert.DoesNotContain("Globex", view.Markup);
    }

    [Fact]
    public void ATeamKey_ShowsThatTeam_NotTheSelectedOne()
    {
        Setup([AcmeTeam(), GlobexTeam()]);

        var view = Render<TeamMembersView<Member>>(p => p.Add(x => x.TeamKey, Globex));

        view.WaitForAssertion(() => Assert.Single(Rows(view)));
    }

    [Fact]
    public void WithNoSelectedTeam_AndNoKey_ItSaysThereIsNone()
    {
        Setup([AcmeTeam()], selected: null);

        var view = Render<TeamMembersView<Member>>();

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-no-team]")));
    }

    [Fact]
    public void AKeyThatIsNotAmongTheCallersTeams_SaysThereIsNone()
    {
        Setup([AcmeTeam()]);

        var view = Render<TeamMembersView<Member>>(p => p.Add(x => x.TeamKey, "nope"));

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-no-team]")));
    }

    /// <summary>Alone, the view carries what the team's card did: the team's actions above the members.</summary>
    [Fact]
    public void Alone_TheTeamsActionsAreAboveTheMembers()
    {
        Setup([AcmeTeam()]);

        var view = Render<TeamMembersView<Member>>();

        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-team-header]")));
        Assert.NotEmpty(view.FindComponents<TeamActionsMenu<Member>>());
        Assert.NotEmpty(view.FindComponents<TeamConsentControl<Member>>());
    }

    [Fact]
    public void WithoutTheHeader_OnlyTheMembersAreDrawn()
    {
        Setup([AcmeTeam()]);

        var view = Render<TeamMembersView<Member>>(p => p.Add(x => x.ShowHeader, false));

        view.WaitForAssertion(() => Assert.Equal(2, Rows(view).Length));
        Assert.Empty(view.FindAll("[data-team-header]"));
    }

    [Fact]
    public void TheMemberParameters_ReachTheGrid()
    {
        Setup([AcmeTeam()]);

        var view = Render<TeamMembersView<Member>>(p => p.Add(x => x.MemberColumns, b =>
        {
            b.OpenComponent<RadzenDataGridColumn<Member>>(0);
            b.AddAttribute(1, "Title", "Region");
            b.CloseComponent();
        }));

        view.WaitForAssertion(() => Assert.Contains("Region", view.Markup));
    }

    [Fact]
    public void TheCurrentMembersRow_IsMarked()
    {
        Setup([AcmeTeam()]);

        var view = Render<TeamMembersView<Member>>();

        view.WaitForAssertion(() => Assert.Single(view.FindAll("tr[data-tharga-current-member]")));
    }

    // --- TeamComponent draws its teams with the same view ---

    [Fact]
    public void TheTeamList_DrawsEachTeamsMembersWithTheView_FromWhatItReadOnce()
    {
        Setup([AcmeTeam(), GlobexTeam()]);

        var list = Render<TeamComponent<Member>>();

        list.WaitForAssertion(() => Assert.NotEmpty(list.FindComponents<TeamMembersView<Member>>()));
        Assert.All(list.FindComponents<TeamMembersView<Member>>(), x => Assert.False(x.Instance.ShowHeader));
    }

    [Fact]
    public void TheSwitchToAllTeams_IsOffered_WhenOnlyTheSelectedTeamIsShown()
    {
        Setup([AcmeTeam(), GlobexTeam()]);

        var list = Render<TeamComponent<Member>>(p => p.Add(x => x.ShowSelectedTeamOnly, true));

        list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll("[name=showAllTeams]")));
        Assert.DoesNotContain("Globex", list.Markup);
    }

    [Fact]
    public void TheSwitchToAllTeams_CanBeLeftOut()
    {
        Setup([AcmeTeam(), GlobexTeam()]);

        var list = Render<TeamComponent<Member>>(p => p
            .Add(x => x.ShowSelectedTeamOnly, true)
            .Add(x => x.ShowAllTeamsSwitch, false));

        list.WaitForAssertion(() => Assert.NotEmpty(list.FindComponents<TeamMembersView<Member>>()));
        Assert.Empty(list.FindAll("[name=showAllTeams]"));
        Assert.DoesNotContain("Globex", list.Markup);
    }

    [Fact]
    public void WithoutShowSelectedTeamOnly_NothingChanges_AndTheSwitchIsNotThere()
    {
        Setup([AcmeTeam(), GlobexTeam()]);

        var list = Render<TeamComponent<Member>>();

        list.WaitForAssertion(() => Assert.Contains("Globex", list.Markup));
        Assert.Empty(list.FindAll("[name=showAllTeams]"));
    }

    [Fact]
    public void TheListsCreateButton_IsTheCreateTeamButton()
    {
        Setup([AcmeTeam()]);

        var list = Render<TeamComponent<Member>>();

        list.WaitForAssertion(() => Assert.Single(list.FindComponents<CreateTeamButton>()));
    }

    // --- CreateTeamButton on its own ---

    [Fact]
    public void TheCreateButton_IsShown_WhereTeamCreationIsAllowed()
    {
        Setup([AcmeTeam()]);

        var button = Render<CreateTeamButton>();

        Assert.NotEmpty(button.FindComponents<RadzenButton>());
        Assert.Contains(TeamComponentText.CreateTeam.Default, button.Markup);
    }

    [Fact]
    public void TheCreateButton_IsNotShown_WhereTeamCreationIsNot()
    {
        Setup([AcmeTeam()], allowCreation: false);

        var button = Render<CreateTeamButton>();

        Assert.Empty(button.FindComponents<RadzenButton>());
    }

    [Fact]
    public void TheCreateButton_CanSayWhatItDoes()
    {
        Setup([AcmeTeam()]);

        var button = Render<CreateTeamButton>(p => p.Add(x => x.Text, "Start a team"));

        Assert.Contains("Start a team", button.Markup);
    }

    /// <summary>The callback wins over the path and over the built-in create, as it does in the list.</summary>
    [Fact]
    public void ACallback_TakesPrecedence_OverThePathAndTheBuiltInCreate()
    {
        Setup([AcmeTeam()], createPath: "/new-team");
        var requested = 0;

        var button = Render<CreateTeamButton>(p => p.Add(x => x.CreateTeamRequested, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => requested++)));
        button.Find("button").Click();

        Assert.Equal(1, requested);
        _lifecycle.Verify(x => x.CreateTeamAsync(), Times.Never);
        Assert.EndsWith("/", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public void WithAPath_TheButtonGoesThere()
    {
        Setup([AcmeTeam()], createPath: "new-team");

        var button = Render<CreateTeamButton>();
        button.Find("button").Click();

        Assert.EndsWith("/new-team", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        _lifecycle.Verify(x => x.CreateTeamAsync(), Times.Never);
    }

    [Fact]
    public void TheBuiltInCreate_MakesATeam_AndSaysSo()
    {
        Setup([AcmeTeam()]);
        var created = 0;

        var button = Render<CreateTeamButton>(p => p.Add(x => x.Created, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => created++)));
        button.Find("button").Click();

        button.WaitForAssertion(() => Assert.Equal(1, created));
        _lifecycle.Verify(x => x.CreateTeamAsync(), Times.Once);
    }
}
