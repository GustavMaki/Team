using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// A request for access made from a case is shown in that case, and how it was decided: the customer sees that support asked, and support sees when access was
/// granted and until when (Tharga/Team#379).
/// </summary>
public class SupportCaseAccessRequestObserverTests
{
    private const string TeamKey = "team-a";
    private const string CaseId = "case-1";
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private static TeamAccessRequest Request(TeamAccessRequestStatus status = TeamAccessRequestStatus.Pending, string caseId = CaseId, TimeSpan? duration = null, DateTime? grantedUntil = null) => new()
    {
        Id = "r1",
        RequesterKey = "staff-1",
        RequesterName = "Sam Staff",
        AccessLevel = AccessLevel.Viewer,
        Duration = duration ?? TimeSpan.FromHours(4),
        RequestedAt = Now,
        Status = status,
        GrantedUntil = grantedUntil,
        SupportCaseId = caseId
    };

    [Fact]
    public async Task ARequest_IsShownInItsCase_AsASystemEntry()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request());

        var entry = (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last();
        Assert.Equal(SupportMessageKind.System, entry.Kind);
        Assert.Equal("Sam Staff asked for Viewer access to the team for 4 hours.", entry.Body);
        Assert.Equal("Sam Staff", entry.AuthorName);
        Assert.Equal(Now, entry.SentAt);
    }

    [Fact]
    public async Task AnApproval_SaysUntilWhen_SoBothSidesSeeHowLongAccessLasts()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(TeamAccessRequestStatus.Approved, grantedUntil: new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc)));

        var entry = (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last();
        Assert.Equal("Access to the team was granted at Viewer until 2026-11-17 13:00 UTC.", entry.Body);
        Assert.Null(entry.AuthorName);
    }

    [Fact]
    public async Task AnApprovalWithNoEnd_SaysSo()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(TeamAccessRequestStatus.Approved, grantedUntil: null));

        Assert.Equal("Access to the team was granted at Viewer with no end.", (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last().Body);
    }

    [Fact]
    public async Task ADenial_IsShown()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(TeamAccessRequestStatus.Denied));

        Assert.Equal("The request for access to the team was denied.", (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last().Body);
    }

    [Fact]
    public async Task AWithdrawal_IsShown_InTheRequestersName()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(TeamAccessRequestStatus.Cancelled));

        Assert.Equal("Sam Staff withdrew the request for access to the team.", (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last().Body);
    }

    [Theory]
    [InlineData(1, "for 1 hour")]
    [InlineData(8, "for 8 hours")]
    [InlineData(24, "for 1 day")]
    [InlineData(168, "for 7 days")]
    public async Task TheDuration_ReadsAsItDoesWhereTheRequestIsMade(int hours, string phrase)
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(duration: TimeSpan.FromHours(hours)));

        Assert.EndsWith($"{phrase}.", (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last().Body);
    }

    [Fact]
    public async Task ARequestWithNoEnd_SaysSo()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request() with { Duration = null });

        Assert.EndsWith("with no end.", (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last().Body);
    }

    [Fact]
    public async Task EachChange_TellsTheViewsOfTheCase_SoTheyReload()
    {
        var (observer, _, notifier) = await Build();
        var told = new List<SupportCaseUpdatedEventArgs>();
        notifier.CaseUpdated += (_, e) => told.Add(e);

        await observer.ChangedAsync(TeamKey, Request());

        var change = Assert.Single(told);
        Assert.Equal((TeamKey, CaseId, SupportCaseChange.Replied), (change.TeamKey, change.CaseId, change.Change));
    }

    // --- only a case of that team is shown ---

    [Fact]
    public async Task ARequestWithNoCase_PostsNothing()
    {
        var (observer, cases, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(caseId: null));

        Assert.Single((await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items);
    }

    /// <summary>The case is looked up under the team the request is for, so quoting the id of another team's case writes nothing there.</summary>
    [Fact]
    public async Task ARequestQuotingACaseOfAnotherTeam_PostsNothing_Anywhere()
    {
        var (observer, cases, _) = await Build();
        await cases.AddCaseAsync(Case("other-case", "team-b"), First());

        await observer.ChangedAsync(TeamKey, Request(caseId: "other-case"));

        Assert.Single((await cases.GetMessagesAsync("team-b", "other-case", null, 10)).Items);
    }

    [Fact]
    public async Task ARequestQuotingNoSuchCase_PostsNothing_AndDoesNotThrow()
    {
        var (observer, _, _) = await Build();

        await observer.ChangedAsync(TeamKey, Request(caseId: "no-such-case"));
    }

    [Fact]
    public async Task TheCaseIsNotWaitingOnSupportBecauseOfIt_EvenWhenItHasNoAuthorIdentity()
    {
        // A case opened by mail from a stranger has no author identity, and neither does the entry the toolkit writes.
        var (observer, cases, _) = await Build(authorIdentity: null);

        await observer.ChangedAsync(TeamKey, Request());

        Assert.Equal(SupportMessageKind.System, (await cases.GetMessagesAsync(TeamKey, CaseId, null, 10)).Items.Last().Kind);
    }

    private static async Task<(SupportCaseAccessRequestObserver Observer, InMemorySupportCaseStore Cases, ISupportCaseNotifier Notifier)> Build(string authorIdentity = "alice")
    {
        var cases = new InMemorySupportCaseStore();
        await cases.AddCaseAsync(Case(CaseId, TeamKey, authorIdentity), First(authorIdentity));

        var notifier = new SupportCaseNotifier();
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(new DateTimeOffset(Now));

        return (new SupportCaseAccessRequestObserver(cases, notifier, time), cases, notifier);
    }

    private static SupportCase Case(string id, string teamKey, string authorIdentity = "alice") => new()
    {
        Id = id,
        TeamKey = teamKey,
        AuthorIdentity = authorIdentity,
        AuthorName = "Alice",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = Now.AddHours(-1),
        MessageCount = 1
    };

    private static SupportMessage First(string authorIdentity = "alice") => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        AuthorIdentity = authorIdentity,
        AuthorName = "Alice",
        Body = "It is empty.",
        SentAt = Now.AddHours(-1)
    };
}
