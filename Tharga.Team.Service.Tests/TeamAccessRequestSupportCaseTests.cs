using Microsoft.Extensions.Options;
using Tharga.Team;

namespace Tharga.Team.Service.Tests;

/// <summary>
/// A request for access can be made from a support case, which it then remembers, and what happens to the request is told to the observers (Tharga/Team#379).
/// </summary>
public class TeamAccessRequestSupportCaseTests
{
    private const string TeamKey = "team-1";
    private const string Me = "dev-1";

    // --- the request remembers its case ---

    [Fact]
    public async Task ARequestFromACase_RemembersTheCase()
    {
        var service = Service();

        var returned = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, TimeSpan.FromHours(1), "Support case: Export", "  case-1  ");

        Assert.Equal("case-1", returned.SupportCaseId);
        Assert.Equal("case-1", Assert.Single(service.Team(TeamKey).AccessRequests).SupportCaseId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ARequestWithNoCase_RemembersNone(string supportCaseId)
    {
        var service = Service();

        var returned = await service.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, TimeSpan.FromHours(1), null, supportCaseId);

        Assert.Null(returned.SupportCaseId);
    }

    [Fact]
    public async Task TheOverloadWithoutACase_StillWorks_AndRemembersNone()
    {
        var returned = await Service().RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, TimeSpan.FromHours(1), null);

        Assert.Null(returned.SupportCaseId);
    }

    /// <summary>A hosts own implementation of the contract written before cases existed keeps working for an ordinary request, and refuses one it would drop the case of.</summary>
    [Fact]
    public async Task AnImplementationThatKnowsNoCases_AnswersAnOrdinaryRequest_AndRefusesAnyOther()
    {
        ITeamAccessRequestService old = new OldImplementation();

        Assert.Equal("ordinary", (await old.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null, null)).Id);
        await Assert.ThrowsAsync<NotSupportedException>(() => old.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null, "case-1"));
    }

    // --- the observers ---

    [Fact]
    public async Task ARequest_IsToldToTheObservers_WithItsCase()
    {
        var (sut, inner, observer) = Facade();
        var made = new TeamAccessRequest { Id = "r1", RequesterKey = Me, AccessLevel = AccessLevel.Viewer, RequestedAt = DateTime.UtcNow, SupportCaseId = "case-1" };
        inner.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, Arg.Any<TimeSpan?>(), "why", "case-1").Returns(made);

        var returned = await sut.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, TimeSpan.FromHours(1), "why", "case-1");

        Assert.Same(made, returned);
        await observer.Received(1).ChangedAsync(TeamKey, made, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(TeamAccessRequestStatus.Approved)]
    [InlineData(TeamAccessRequestStatus.Denied)]
    [InlineData(TeamAccessRequestStatus.Cancelled)]
    public async Task ADecision_IsToldToTheObservers_AsItWasStored(TeamAccessRequestStatus status)
    {
        var (sut, _, observer) = Facade(Stored("r1", status));

        switch (status)
        {
            case TeamAccessRequestStatus.Approved: await sut.ApproveTeamAccessRequestAsync(TeamKey, "r1"); break;
            case TeamAccessRequestStatus.Denied: await sut.DenyTeamAccessRequestAsync(TeamKey, "r1"); break;
            default: await sut.CancelTeamAccessRequestAsync(TeamKey, "r1"); break;
        }

        await observer.Received(1).ChangedAsync(TeamKey, Arg.Is<TeamAccessRequest>(x => x.Id == "r1" && x.Status == status && x.SupportCaseId == "case-1"), Arg.Any<CancellationToken>());
    }

    /// <summary>The change is made and authorized below; nothing that failed is announced.</summary>
    [Fact]
    public async Task ARefusedDecision_IsToldToNobody()
    {
        var (sut, inner, observer) = Facade(Stored("r1", TeamAccessRequestStatus.Pending));
        inner.ApproveTeamAccessRequestAsync(TeamKey, "r1", Arg.Any<string[]>()).Returns<Task>(_ => throw new UnauthorizedAccessException());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.ApproveTeamAccessRequestAsync(TeamKey, "r1"));

        await observer.DidNotReceiveWithAnyArgs().ChangedAsync(default, default, default);
    }

    [Fact]
    public async Task AnObserverThatFails_NeverUndoesOrHidesTheChange_AndTheOthersAreStillTold()
    {
        var failing = Substitute.For<ITeamAccessRequestObserver>();
        failing.ChangedAsync(Arg.Any<string>(), Arg.Any<TeamAccessRequest>(), Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("down"));
        var (sut, _, observer) = Facade(failing, Stored("r1", TeamAccessRequestStatus.Denied));

        await sut.DenyTeamAccessRequestAsync(TeamKey, "r1");

        await observer.Received(1).ChangedAsync(TeamKey, Arg.Any<TeamAccessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoObservers_RequestingAndDecidingWorkAsBefore()
    {
        var inner = Substitute.For<ITeamService>();
        inner.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null, null).Returns(new TeamAccessRequest { Id = "r1", RequesterKey = Me, AccessLevel = AccessLevel.Viewer, RequestedAt = DateTime.UtcNow });
        var sut = new TeamManagementService<TestMember>(inner, null, null, null, null, Options.Create(new ConsentOptions { Roles = ["Support"] }));

        Assert.Equal("r1", (await sut.RequestTeamAccessAsync(TeamKey, AccessLevel.Viewer, null, null)).Id);
        await sut.DenyTeamAccessRequestAsync(TeamKey, "r1");
    }

    private static TeamAccessRequest Stored(string id, TeamAccessRequestStatus status) => new()
    {
        Id = id,
        RequesterKey = Me,
        AccessLevel = AccessLevel.Viewer,
        RequestedAt = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc),
        Status = status,
        SupportCaseId = "case-1"
    };

    private static (TeamManagementService<TestMember> Sut, ITeamService Inner, ITeamAccessRequestObserver Observer) Facade(params TeamAccessRequest[] stored)
        => Facade(null, stored);

    private static (TeamManagementService<TestMember> Sut, ITeamService Inner, ITeamAccessRequestObserver Observer) Facade(ITeamAccessRequestObserver first, params TeamAccessRequest[] stored)
    {
        var inner = Substitute.For<ITeamService>();
        inner.GetTeamByKeyAsync(TeamKey).Returns(new TestTeam { Key = TeamKey, Name = "Team", Members = [], AccessRequests = stored });

        var observer = Substitute.For<ITeamAccessRequestObserver>();
        ITeamAccessRequestObserver[] observers = first == null ? [observer] : [first, observer];

        var sut = new TeamManagementService<TestMember>(inner, null, null, null, null, Options.Create(new ConsentOptions { Roles = ["Support"] }), requestObservers: observers);

        return (sut, inner, observer);
    }

    private static TestTeamService Service()
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns(Me);
        user.Name.Returns("Dev One");
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync().Returns(user);

        var service = new TestTeamService(userService);
        service.AddTeam(TeamKey, "Team", new TestMember { Key = "owner-1", AccessLevel = AccessLevel.Owner, State = MembershipState.Member });

        return service;
    }

    private sealed class OldImplementation : ITeamAccessRequestService
    {
        public Task<TeamAccessRequest> RequestTeamAccessAsync(string teamKey, AccessLevel accessLevel, TimeSpan? duration, string message)
            => Task.FromResult(new TeamAccessRequest { Id = "ordinary", RequesterKey = Me, AccessLevel = accessLevel, RequestedAt = DateTime.UtcNow });

        public Task CancelTeamAccessRequestAsync(string teamKey, string requestId) => Task.CompletedTask;

        public Task<IReadOnlyList<TeamAccessRequestItem>> GetMyAccessRequestsAsync() => Task.FromResult<IReadOnlyList<TeamAccessRequestItem>>([]);

        public Task<IReadOnlyList<TeamAccessRequestItem>> GetAccessRequestsAwaitingMeAsync() => Task.FromResult<IReadOnlyList<TeamAccessRequestItem>>([]);
    }
}
