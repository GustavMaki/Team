using MongoDB.Bson;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>The support case a request was made from is stored with it, and a request made from nowhere stores nothing for it (Tharga/Team#379).</summary>
public class TeamAccessRequestSupportCaseMappingTests
{
    private static TeamAccessRequest Request(string supportCaseId) => new()
    {
        Id = "r1",
        RequesterKey = "staff-1",
        AccessLevel = AccessLevel.Viewer,
        RequestedAt = new DateTime(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc),
        SupportCaseId = supportCaseId
    };

    [Fact]
    public void TheCase_RoundTrips()
    {
        var stored = TeamAccessRequestEntity.FromContract(Request("case-1"));

        Assert.Equal("case-1", stored.ToBsonDocument()[nameof(TeamAccessRequestEntity.SupportCaseId)].AsString);
        Assert.Equal("case-1", stored.ToContract().SupportCaseId);
    }

    /// <summary>Everything requested before cases were remembered reads as one made from none.</summary>
    [Fact]
    public void ARequestFromNoCase_StoresNothing_AndReadsAsHavingNone()
    {
        var stored = TeamAccessRequestEntity.FromContract(Request(null));

        Assert.False(stored.ToBsonDocument().Contains(nameof(TeamAccessRequestEntity.SupportCaseId)));
        Assert.Null(stored.ToContract().SupportCaseId);
    }
}
