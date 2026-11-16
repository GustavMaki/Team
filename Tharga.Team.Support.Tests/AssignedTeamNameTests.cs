using System.Security.Claims;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Giving an unassigned case a team by hand records the team's name next to its key (Tharga/Team#500), so the case reads like one that was
/// placed on receipt.
/// </summary>
public class AssignedTeamNameTests
{
    private const string Alice = "alice-subject";

    [Fact]
    public async Task AnAssignedCase_CarriesBothTheTeamKeyAndTheTeamName()
    {
        var (store, caseId) = await StoreWithAnUnassignedCase();
        var service = Build(store, OversightOf(("acme", "Acme AB")));

        Assert.True(await service.AssignCaseAsync(caseId, "acme"));

        var assigned = await store.GetCaseByIdAsync(caseId);
        Assert.Equal("acme", assigned.TeamKey);
        Assert.Equal("Acme AB", assigned.TeamName);
    }

    /// <summary>The staff who triage the queue are usually not members of the team they give a case to, so the name comes from the cross-team read.</summary>
    [Fact]
    public async Task TheNameIsFoundForATeamTheStaffAreNotMembersOf()
    {
        var (store, caseId) = await StoreWithAnUnassignedCase();
        var service = Build(store, OversightOf(("other", "Other AB"), ("acme", "Acme AB")));

        await service.AssignCaseAsync(caseId, "acme");

        Assert.Equal("Acme AB", (await store.GetCaseByIdAsync(caseId)).TeamName);
    }

    /// <summary>A name that cannot be read must not stop the case being assigned: it carries the key, as it did before the name was recorded.</summary>
    [Fact]
    public async Task WhenTheNameCannotBeRead_TheCaseIsStillAssigned()
    {
        var (store, caseId) = await StoreWithAnUnassignedCase();
        var oversight = Substitute.For<ITeamOversightService>();
        oversight.GetAllTeamsAsync().Returns(_ => Throwing());
        var service = Build(store, oversight);

        Assert.True(await service.AssignCaseAsync(caseId, "acme"));

        var assigned = await store.GetCaseByIdAsync(caseId);
        Assert.Equal("acme", assigned.TeamKey);
        Assert.Null(assigned.TeamName);
    }

    [Fact]
    public async Task WithNoTeamDirectoryAtAll_TheCaseIsStillAssigned()
    {
        var (store, caseId) = await StoreWithAnUnassignedCase();

        Assert.True(await Build(store, null).AssignCaseAsync(caseId, "acme"));

        Assert.Equal("acme", (await store.GetCaseByIdAsync(caseId)).TeamKey);
    }

    /// <summary>The service hands the store the name it found, together with the key.</summary>
    [Fact]
    public async Task TheStoreIsGivenTheNameWithTheKey()
    {
        var store = Substitute.For<ISupportCaseStore>();
        store.TryAssignCaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = Build(store, OversightOf(("acme", "Acme AB")));

        Assert.True(await service.AssignCaseAsync("case-1", "acme"));
        await store.Received(1).TryAssignCaseAsync("case-1", "acme", "Acme AB", Arg.Any<SupportMessage>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ITeam> Throwing()
    {
        await Task.Yield();
        throw new UnauthorizedAccessException("teams:read is needed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static ITeamOversightService OversightOf(params (string Key, string Name)[] teams)
    {
        var oversight = Substitute.For<ITeamOversightService>();
        oversight.GetAllTeamsAsync().Returns(_ => Teams(teams));

        return oversight;
    }

    private static async IAsyncEnumerable<ITeam> Teams((string Key, string Name)[] teams)
    {
        await Task.Yield();
        foreach (var (key, name) in teams)
        {
            var team = Substitute.For<ITeam>();
            team.Key.Returns(key);
            team.Name.Returns(name);
            yield return team;
        }
    }

    private static async Task<(InMemorySupportCaseStore Store, string CaseId)> StoreWithAnUnassignedCase()
    {
        var store = new InMemorySupportCaseStore();

        await store.AddCaseAsync(new SupportCase
        {
            Id = "case-1",
            TeamKey = null,
            AuthorIdentity = null,
            Subject = "Key expired",
            AuthorName = "stranger@example.com",
            Status = SupportCaseStatus.Open,
            CreatedAt = DateTime.UtcNow,
            MessageCount = 1
        }, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorName = "stranger@example.com",
            Body = "It says my key expired.",
            SentAt = DateTime.UtcNow,
            Source = SupportChannelType.Email
        });

        return (store, "case-1");
    }

    private static ISupportCaseService Build(ISupportCaseStore store, ITeamOversightService oversight)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Alice),
            new Claim(ClaimTypes.Name, Alice),
            new Claim(TeamClaimTypes.SystemScope, SystemSupportScopes.Manage)
        ], "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));

        return new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, TimeProvider.System, teamOversight: oversight),
            authorizer);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
