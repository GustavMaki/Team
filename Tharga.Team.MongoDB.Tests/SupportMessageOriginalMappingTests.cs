using MongoDB.Bson;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>The id of a message's stored original round-trips, and an entry that has none stores nothing for it (Tharga/Team#499).</summary>
public class SupportMessageOriginalMappingTests
{
    private static SupportMessage Received(string originalId) => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorName = "Alice",
        Body = "It is empty.",
        SentAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        OriginalId = originalId
    };

    [Fact]
    public void AnOriginal_RoundTrips_AndTheMessageSaysItHasOne()
    {
        var stored = MongoSupportCaseStore.ToEntity(Received("orig-1"));

        Assert.Equal("orig-1", stored.ToBsonDocument()[nameof(SupportMessageEntity.OriginalId)].AsString);

        var read = MongoSupportCaseStore.ToMessage(stored);
        Assert.Equal("orig-1", read.OriginalId);
        Assert.True(read.HasOriginal);
    }

    /// <summary>Everything received before originals were kept reads as one with none.</summary>
    [Fact]
    public void AnEntryWithNoOriginal_StoresNothing_AndReadsAsHavingNone()
    {
        var stored = MongoSupportCaseStore.ToEntity(Received(null));

        Assert.False(stored.ToBsonDocument().Contains(nameof(SupportMessageEntity.OriginalId)));
        Assert.False(MongoSupportCaseStore.ToMessage(stored).HasOriginal);
    }
}
