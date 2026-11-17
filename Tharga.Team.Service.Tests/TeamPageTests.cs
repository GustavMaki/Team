using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// The caller's teams can be read a page at a time without their rosters, filtered as the full read is, and by a service that knows nothing of
/// pages (Tharga/Team#403).
/// </summary>
public class TeamPageTests
{
    private static TestTeam Team(string key, string name) => new() { Key = key, Name = name, Members = [] };

    // --- the order and the cursor ---

    [Fact]
    public void ThePages_ReadByNameThenKey_AndTheLastHasNoCursor()
    {
        ITeam[] teams = [Team("b", "Beta"), Team("a2", "Acme"), Team("a1", "Acme"), Team("c", "Core")];

        var first = TeamPaging.Slice(teams, new TeamPageRequest(null, 3));
        var second = TeamPaging.Slice(teams, new TeamPageRequest(first.NextCursor, 3));

        Assert.Equal(["a1", "a2", "b"], first.Items.Select(x => x.Key));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["c"], second.Items.Select(x => x.Key));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public void AnExactlyFullLastPage_HasNoCursor_SoNobodyReadsAnEmptyOne()
    {
        var page = TeamPaging.Slice<ITeam>([Team("a", "A"), Team("b", "B")], new TeamPageRequest(null, 2));

        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(50, 50)]
    [InlineData(100000, 500)]
    public void ThePageSize_IsHeldToWhatAPageMayBe(int asked, int used)
        => Assert.Equal(used, new TeamPageRequest(null, asked).Size);

    [Fact]
    public void ACursorNobodyIssued_IsRefused()
        => Assert.Throws<ArgumentException>(() => TeamPaging.Parse("not a cursor"));

    [Fact]
    public void ACursor_SurvivesAnyNameAndKey()
    {
        var parsed = TeamPaging.Parse(TeamPaging.CursorOf(new TeamSummary { Key = "k:1/é", Name = "Åke & \"Co\"" }));

        Assert.Equal(("Åke & \"Co\"", "k:1/é"), parsed);
    }

    [Fact]
    public void ASummary_KeepsWhoTheTeamIsAndWhatConsentItHolds_NotItsMembers()
    {
        var team = new TestTeam { Key = "a", Name = "A", Members = [new TestMember { Key = "m" }], ConsentedRoles = ["Support"], ConsentAccessLevel = AccessLevel.Viewer };

        var summary = TeamSummary.Of(team);

        Assert.Equal(("a", "A"), (summary.Key, summary.Name));
        Assert.Equal(["Support"], summary.ConsentedRoles);
        Assert.Equal(AccessLevel.Viewer, summary.ConsentAccessLevel);
    }

    // --- the base service pages what it can already list ---

    [Fact]
    public async Task AServiceThatListsTeams_AnswersAPage_WithoutAnyPagingOfItsOwn()
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns("me");
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync().Returns(user);
        var service = new TestTeamService(users);
        service.AddTeam("b", "Beta", new TestMember { Key = "me", AccessLevel = AccessLevel.Owner, State = MembershipState.Member });
        service.AddTeam("a", "Acme", new TestMember { Key = "me", AccessLevel = AccessLevel.Owner, State = MembershipState.Member });

        var page = await ((ITeamService)service).GetTeamPageAsync(new TeamPageRequest(null, 1));

        Assert.Equal(["Acme"], page.Items.Select(x => x.Name));
        Assert.NotNull(page.NextCursor);
    }

    // --- the facade filters a page as it filters the list ---

    [Fact]
    public async Task APageOmitsATeamTheCallerMayNotRead_AndIsToppedUpFromTheNext()
    {
        var (sut, inner) = Facade();
        var teams = new[] { Mine("a", "A"), Team("b", "B"), Mine("c", "C"), Mine("d", "D") };
        inner.GetTeamPageAsync<TestMember>(Arg.Any<TeamPageRequest>()).Returns(call =>
        {
            var (items, next) = TeamPaging.Slice(teams, (TeamPageRequest)call[0]);
            return new TeamPageOf<TestMember>([.. items], next);
        });

        var page = await sut.GetTeamPageAsync(new TeamPageRequest(null, 2));

        Assert.Equal(["a", "c"], page.Items.Select(x => x.Key));
        Assert.NotNull(page.NextCursor);
        var rest = await sut.GetTeamPageAsync(new TeamPageRequest(page.NextCursor, 2));
        Assert.Equal(["d"], rest.Items.Select(x => x.Key));
        Assert.Null(rest.NextCursor);
    }

    [Fact]
    public async Task ARoster_IsNeverPartOfWhatTheCallerGets()
    {
        var (sut, inner) = Facade();
        inner.GetTeamPageAsync<TestMember>(Arg.Any<TeamPageRequest>())
            .Returns(new TeamPageOf<TestMember>([Mine("a", "A") with { Members = [.. Mine("a", "A").Members, new TestMember { Key = "other" }] }], null));

        var page = await sut.GetTeamPageAsync(null);

        Assert.IsType<TeamSummary>(Assert.Single(page.Items));
    }

    [Fact]
    public async Task WhenEveryTeamIsRefused_ThePageIsEmpty_AndTheReadEnds()
    {
        var (sut, inner) = Facade();
        var calls = 0;
        inner.GetTeamPageAsync<TestMember>(Arg.Any<TeamPageRequest>()).Returns(_ =>
            calls++ < 3 ? new TeamPageOf<TestMember>([Team("x", "X")], "next-" + calls) : new TeamPageOf<TestMember>([], null));

        var page = await sut.GetTeamPageAsync(new TeamPageRequest(null, 2));

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    // --- reading every page ---

    [Fact]
    public async Task AllTheTeams_AreReadPageByPage_UntilThereIsNoCursor()
    {
        var directory = Substitute.For<ITeamDirectoryService>();
        directory.GetTeamPageAsync(Arg.Any<TeamPageRequest>()).Returns(call => ((TeamPageRequest)call[0]).Cursor switch
        {
            null => new TeamPage([new TeamSummary { Key = "a", Name = "A" }], "c1"),
            "c1" => new TeamPage([new TeamSummary { Key = "b", Name = "B" }], "c2"),
            _ => new TeamPage([new TeamSummary { Key = "c", Name = "C" }], null)
        });

        var all = await directory.GetAllTeamSummariesAsync();

        Assert.Equal(["a", "b", "c"], all.Select(x => x.Key));
        await directory.Received(3).GetTeamPageAsync(Arg.Is<TeamPageRequest>(x => x.PageSize == TeamPageRequest.MaxPageSize));
    }

    private static TestTeam Mine(string key, string name)
        => new() { Key = key, Name = name, Members = [new TestMember { Key = "me", AccessLevel = AccessLevel.User, State = MembershipState.Member }] };

    private static (TeamManagementService<TestMember> Sut, ITeamService Inner) Facade()
    {
        var inner = Substitute.For<ITeamService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns("me");
        var users = Substitute.For<IUserService>();
        users.GetCurrentUserAsync().Returns(user);
        var registry = new ScopeRegistry();
        registry.Register(TeamScopes.Read, AccessLevel.Viewer);

        return (new TeamManagementService<TestMember>(inner, users, registry, null, null, Options.Create(new ConsentOptions())), inner);
    }
}
