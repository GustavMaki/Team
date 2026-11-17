using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// The audit log can be read as units of work, one group per correlation id, so one action is one row and its traces sit behind it (Tharga/Team#402).
/// </summary>
public class AuditEntryGroupTests
{
    private static readonly DateTime T0 = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private static AuditEntry Entry(int seconds, AuditEventType type = AuditEventType.ServiceCall, bool success = true, string method = "M")
        => new() { Timestamp = T0.AddSeconds(seconds), EventType = type, Success = success, MethodName = method, CorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111") };

    // --- what a group is ---

    [Fact]
    public void TheLead_IsTheFirstDomainEntry_NotTheTraceThatCameFirst()
    {
        var trace = Entry(0, AuditEventType.ServiceCall, method: "trace");
        var change = Entry(1, AuditEventType.DataChange, method: "change");

        var group = AuditEntryGroup.Of([change, trace]);

        Assert.Same(change, group.Lead);
    }

    [Fact]
    public void WithNoDomainEntry_TheLead_IsTheFirstEntryWritten()
    {
        var first = Entry(0);
        var group = AuditEntryGroup.Of([Entry(5), first, Entry(2)]);

        Assert.Same(first, group.Lead);
    }

    [Fact]
    public void TheOthers_AreEveryEntryButTheLead_WithTheTracesLast()
    {
        var group = AuditEntryGroup.Of([Entry(0), Entry(1, AuditEventType.DataChange, method: "first"), Entry(2, AuditEventType.DataChange, method: "second"), Entry(3)]);

        Assert.Equal(["second", "M", "M"], group.Others.Select(x => x.MethodName));
        Assert.Equal(4, group.Count);
    }

    [Fact]
    public void AGroup_IsFailed_WhenAnyEntryFailed_AndBeganWithItsFirst()
    {
        var group = AuditEntryGroup.Of([Entry(3), Entry(1, success: false), Entry(2)]);

        Assert.True(group.Failed);
        Assert.Equal(T0.AddSeconds(1), group.Started);
        Assert.False(AuditEntryGroup.Of([Entry(0)]).Failed);
    }

    [Fact]
    public void TheEntries_AreKeptInTheOrderTheyWereWritten()
        => Assert.Equal([0, 1, 2], AuditEntryGroup.Of([Entry(2), Entry(0), Entry(1)]).Entries.Select(x => (int)(x.Timestamp - T0).TotalSeconds));

    // --- a logger that cannot fold ---

    [Fact]
    public async Task ALoggerThatCannotGroup_AnswersAGroupPerEntry()
    {
        IAuditLogger logger = new FlatLogger(Entry(0), Entry(1));

        var result = await logger.QueryGroupsAsync(new AuditQuery());

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, x => Assert.Equal(1, x.Count));
        Assert.Equal(2, result.TotalCount);
    }

    // --- the services ---

    private static string Rendered(FilterDefinition<AuditEntryEntity> filter)
        => filter.Render(new RenderArgs<AuditEntryEntity>(BsonSerializer.LookupSerializer<AuditEntryEntity>(), BsonSerializer.SerializerRegistry)).ToString();

    private static (CompositeAuditLogger Composite, IAuditRepositoryCollection Collection, List<FilterDefinition<AuditEntryEntity>> Filters) Store()
    {
        var collection = Substitute.For<IAuditRepositoryCollection>();
        var filters = new List<FilterDefinition<AuditEntryEntity>>();
        collection.GetGroupsAsync(Arg.Do<FilterDefinition<AuditEntryEntity>>(filters.Add), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => ((IReadOnlyList<IReadOnlyList<AuditEntryEntity>>)[[new AuditEntryEntity { Timestamp = T0, CorrelationId = Guid.NewGuid() }, new AuditEntryEntity { Timestamp = T0.AddSeconds(1), CorrelationId = Guid.NewGuid() }]], 7));
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IAuditRepositoryCollection)).Returns(collection);
        var mongo = new MongoDbAuditLogger(provider, Substitute.For<Microsoft.Extensions.Logging.ILogger<MongoDbAuditLogger>>());

        return (new CompositeAuditLogger([mongo], Options.Create(new AuditOptions())), collection, filters);
    }

    [Fact]
    public async Task TheTeamBoundRead_TakesTheTeamFromTheArgument_NotTheQuery()
    {
        var (composite, _, filters) = Store();

        await new AuditReadService(composite).QueryGroupsAsync("team-a", new AuditQuery { TeamKey = "team-b" });

        var filter = Rendered(Assert.Single(filters));
        Assert.Contains("team-a", filter);
        Assert.DoesNotContain("team-b", filter);
    }

    [Fact]
    public async Task TheOversightRead_HonoursATeamOnTheQueryAsAFilter()
    {
        var (composite, _, filters) = Store();

        await new AuditReadService(composite).QueryAllGroupsAsync(new AuditQuery { TeamKey = "team-b" });

        Assert.Contains("team-b", Rendered(Assert.Single(filters)));
    }

    [Fact]
    public async Task TheStoresGroups_AreAnswered_AsGroupsOfTheirEntries_WithTheGroupCount()
    {
        var (composite, collection, _) = Store();

        var result = await composite.QueryGroupsAsync(new AuditQuery { Skip = 16, Take = 8, SortDescending = false });

        var group = Assert.Single(result.Items);
        Assert.Equal(2, group.Count);
        Assert.Equal(7, result.TotalCount);
        await collection.Received(1).GetGroupsAsync(Arg.Any<FilterDefinition<AuditEntryEntity>>(), 16, 8, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoStore_TheGroupedRead_IsEmpty()
    {
        var composite = new CompositeAuditLogger([], Options.Create(new AuditOptions()));

        var result = await composite.QueryGroupsAsync(new AuditQuery());

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task AReadServiceThatKnowsNoGroups_RefusesRatherThanAnsweringWrongly()
    {
        IAuditReadService old = new OldReadService();

        await Assert.ThrowsAsync<NotSupportedException>(() => old.QueryGroupsAsync("team-a", new AuditQuery()));
    }

    [Fact]
    public void TheGroupedReads_AreGatedLikeTheFlatOnes()
    {
        Assert.NotNull(typeof(IAuditReadService).GetMethod(nameof(IAuditReadService.QueryGroupsAsync))!.GetCustomAttributes(typeof(RequireScopeAttribute), false).SingleOrDefault());
        Assert.NotNull(typeof(IAuditOversightService).GetMethod(nameof(IAuditOversightService.QueryAllGroupsAsync))!.GetCustomAttributes(typeof(RequireScopeAttribute), false).SingleOrDefault());
    }

    // --- what the database is asked ---

    /// <summary>Stands in for the driver's own value, which needs the host's Guid representation to be written.</summary>
    private static readonly BsonValue Empty = new BsonString("empty-correlation");

    private static RenderArgs<AuditEntryEntity> Args => new(BsonSerializer.LookupSerializer<AuditEntryEntity>(), BsonSerializer.SerializerRegistry);

    [Fact]
    public void ThePage_IsGroupedByCorrelationId_AndPagedByGroup()
    {
        var pipeline = AuditRepositoryCollection.GroupPage(Builders<AuditEntryEntity>.Filter.Empty, 16, 8, newestFirst: true, Empty).Render(Args);

        var names = pipeline.Documents.Select(x => x.GetElement(0).Name).ToArray();
        Assert.Equal(["$match", "$group", "$sort", "$skip", "$limit"], names);
        Assert.Equal(-1, pipeline.Documents[2]["$sort"]["Started"].AsInt32);
        Assert.Equal(16, pipeline.Documents[3]["$skip"].AsInt32);
        Assert.Equal(8, pipeline.Documents[4]["$limit"].AsInt32);
        Assert.Equal("$CorrelationId", pipeline.Documents[1]["$group"]["_id"]["CorrelationId"].AsString);
        Assert.Equal("$Timestamp", pipeline.Documents[1]["$group"]["Started"]["$min"].AsString);
    }

    [Fact]
    public void TheOldestFirst_SortsTheOtherWay()
    {
        var pipeline = AuditRepositoryCollection.GroupPage(Builders<AuditEntryEntity>.Filter.Empty, 0, 8, newestFirst: false, Empty).Render(Args);

        Assert.Equal(1, pipeline.Documents[2]["$sort"]["Started"].AsInt32);
    }

    /// <summary>Entries from before 3.21 have no correlation id; folding them together would make one giant group of unrelated history.</summary>
    [Fact]
    public void AnEntryWithNoCorrelationId_StandsAloneByItsOwnId()
    {
        var pipeline = AuditRepositoryCollection.GroupPage(Builders<AuditEntryEntity>.Filter.Empty, 0, 8, true, Empty).Render(Args);

        var entryId = pipeline.Documents[1]["$group"]["_id"]["EntryId"]["$cond"].AsBsonArray;
        Assert.Equal("$_id", entryId[1].AsString);
        Assert.True(entryId[2].IsBsonNull);
        Assert.Equal("$CorrelationId", entryId[0]["$eq"][0].AsString);
        Assert.Equal(Empty, entryId[0]["$eq"][1]);
    }

    [Fact]
    public void TheCount_IsOfGroups_NotOfEntries()
    {
        var pipeline = AuditRepositoryCollection.GroupCount(Builders<AuditEntryEntity>.Filter.Empty, Empty).Render(Args);

        Assert.Equal(["$match", "$group", "$count"], pipeline.Documents.Select(x => x.GetElement(0).Name));
        Assert.Equal("n", pipeline.Documents[2]["$count"].AsString);
    }

    [Fact]
    public void TheFilters_AreTheOnesTheFlatReadUses()
    {
        var filter = MongoDbAuditLogger.BuildFilter(new AuditQuery { TeamKey = "team-a", Success = false });
        var pipeline = AuditRepositoryCollection.GroupPage(filter, 0, 8, true, Empty).Render(Args);

        var match = pipeline.Documents[0]["$match"].ToString();
        Assert.Contains("team-a", match);
        Assert.Contains("\"Success\" : false", match);
    }

    private sealed class FlatLogger(params AuditEntry[] entries) : IAuditLogger
    {
        public void Log(AuditEntry entry)
        {
        }

        public Task<AuditQueryResult> QueryAsync(AuditQuery query) => Task.FromResult(new AuditQueryResult { Items = entries, TotalCount = entries.Length });
    }

    private sealed class OldReadService : IAuditReadService
    {
        public Task<AuditQueryResult> QueryAsync(string teamKey, AuditQuery query) => Task.FromResult(new AuditQueryResult());
    }
}
