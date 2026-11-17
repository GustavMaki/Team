using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using TeamEntity = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.TeamEntity;
using Member = Tharga.Team.MongoDB.Tests.MongoSupportSenderDirectoryTests.Member;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// A page of a caller's teams is cut by the database and carries no roster: only the caller's own member entry stays, which the read filter needs
/// (Tharga/Team#403).
/// </summary>
public class TeamPageQueryTests
{
    private static RenderArgs<TeamEntity> Args => new(BsonSerializer.LookupSerializer<TeamEntity>(), BsonSerializer.SerializerRegistry);

    private static string Render(FilterDefinition<TeamEntity> filter) => filter.Render(Args).ToString();

    [Fact]
    public void TheFirstPage_IsTheCallersLiveTeams_AndNothingMore()
    {
        var rendered = Render(TeamRepository<TeamEntity, Member>.PageFilter("u-1", null));

        Assert.Contains("\"DeletedAt\" : null", rendered);
        Assert.Contains("$elemMatch", rendered);
        Assert.DoesNotContain("$gt", rendered);
    }

    [Fact]
    public void ALaterPage_StartsAfterTheLastTeamRead_ByNameThenKey()
    {
        var cursor = TeamPaging.CursorOf(new TeamSummary { Key = "k-2", Name = "Acme" });

        var rendered = Render(TeamRepository<TeamEntity, Member>.PageFilter("u-1", cursor));

        Assert.Contains("\"Name\" : { \"$gt\" : \"Acme\" }", rendered);
        Assert.Contains("\"Key\" : { \"$gt\" : \"k-2\" }", rendered);
    }

    [Fact]
    public void ACursorNobodyIssued_IsRefused()
        => Assert.Throws<ArgumentException>(() => TeamRepository<TeamEntity, Member>.PageFilter("u-1", "not-a-cursor"));

    [Fact]
    public void ThePage_IsOrderedByNameThenKey_OneRowLonger_SoTheLastPageIsKnown()
    {
        var options = TeamRepository<TeamEntity, Member>.PageOptions("u-1", 25);

        Assert.Equal(26, options.Limit);
        Assert.Equal("{ \"Name\" : 1, \"Key\" : 1 }", options.Sort.Render(Args).ToString());
    }

    [Fact]
    public void ThePage_SendsNoRoster_OnlyTheCallersOwnEntry()
    {
        var projection = TeamRepository<TeamEntity, Member>.PageOptions("u-1", 25).Projection.Render(Args).ToString();

        Assert.Contains("\"Members\" : { \"$elemMatch\"", projection);
        Assert.Contains("\"u-1\"", projection);
    }
}
