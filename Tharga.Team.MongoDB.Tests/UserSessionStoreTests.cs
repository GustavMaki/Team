using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The server-side sessions' store (Tharga/Team#471): a session read back as it was started, an end that is true for exactly one caller, activity throttled by
/// the stored stamp so it holds across instances, and entering a team exclusively converging when two instances race. The conditional writes run against
/// <see cref="InMemoryUserSessionCollection"/>, which applies the filters and updates the store sends.
/// </summary>
/// <remarks>
/// "Two instances" is two <see cref="MongoUserSessionStore"/> over one collection, each with nothing of its own, which is what two processes are to the database.
/// Times are fixed; the store is handed them and never reads the clock.
/// </remarks>
public class UserSessionStoreTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserSessionCollection _database = new();

    private MongoUserSessionStore NewInstance() => new(_database.Collection);

    private static UserSession Session(string id, string user = "user-1", DateTime? signedInAt = null, string team = null, DateTime? lastActivity = null)
        => new()
        {
            SessionId = id,
            UserKey = user,
            Subject = $"subject-{user}",
            SignedInAt = signedInAt ?? T0,
            IdentityProvider = "entra",
            AuthenticationMethods = ["pwd", "mfa"],
            Origin = SessionOrigin.SignIn,
            LastActivityAt = lastActivity ?? signedInAt ?? T0,
            ActiveTeamKey = team
        };

    // ---- start and read

    [Fact]
    public async Task ASessionThatWasStarted_IsReadBack_AsItWas()
    {
        var store = NewInstance();
        var session = Session("s1", team: "acme");

        await store.StartAsync(session);
        var read = await NewInstance().GetAsync("s1");

        Assert.NotNull(read);
        Assert.Equal(session.SessionId, read.SessionId);
        Assert.Equal(session.UserKey, read.UserKey);
        Assert.Equal(session.Subject, read.Subject);
        Assert.Equal(session.SignedInAt, read.SignedInAt);
        Assert.Equal(session.IdentityProvider, read.IdentityProvider);
        Assert.Equal(session.AuthenticationMethods, read.AuthenticationMethods);
        Assert.Equal(SessionOrigin.SignIn, read.Origin);
        Assert.Equal(session.LastActivityAt, read.LastActivityAt);
        Assert.Equal("acme", read.ActiveTeamKey);
        Assert.Null(read.EndedAt);
        Assert.Null(read.EndReason);
    }

    [Fact]
    public async Task ASessionThatWasNeverStarted_IsNotFound()
    {
        Assert.Null(await NewInstance().GetAsync("nobody"));
    }

    [Fact]
    public async Task TheOriginAndTheReason_AreStoredByName()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1") with { Origin = SessionOrigin.Adopted });
        await store.EndAsync("s1", SessionEndReason.NewerSignIn, T0.AddMinutes(1));

        var document = _database.Document("s1");

        Assert.Equal("Adopted", document["Origin"].AsString);
        Assert.Equal("NewerSignIn", document["EndReason"].AsString);
    }

    [Fact]
    public void TheEnumsOfTheEntity_DeclareStringRepresentation()
    {
        foreach (var name in new[] { nameof(UserSessionEntity.Origin), nameof(UserSessionEntity.EndReason) })
        {
            var attribute = (BsonRepresentationAttribute)Attribute.GetCustomAttribute(typeof(UserSessionEntity).GetProperty(name)!, typeof(BsonRepresentationAttribute));
            Assert.Equal(BsonType.String, attribute?.Representation);
        }
    }

    // ---- ending

    [Fact]
    public async Task Ending_IsTrueForTheCallThatEndedIt_AndFalseAfter()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));

        Assert.True(await store.EndAsync("s1", SessionEndReason.Idle, T0.AddMinutes(5)));
        Assert.False(await store.EndAsync("s1", SessionEndReason.MaxAge, T0.AddMinutes(6)));
        Assert.False(await NewInstance().EndAsync("s1", SessionEndReason.SignedOut, T0.AddMinutes(7)));
    }

    [Fact]
    public async Task TheFirstEnd_IsKept_Reason_AndTime()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));

        await store.EndAsync("s1", SessionEndReason.Idle, T0.AddMinutes(5));
        await store.EndAsync("s1", SessionEndReason.MaxAge, T0.AddMinutes(6));
        var read = await store.GetAsync("s1");

        Assert.Equal(SessionEndReason.Idle, read.EndReason);
        Assert.Equal(T0.AddMinutes(5), read.EndedAt);
    }

    [Fact]
    public async Task EndingASessionNobodyStarted_IsFalse()
    {
        Assert.False(await NewInstance().EndAsync("nobody", SessionEndReason.Ended, T0));
    }

    /// <summary>Two instances reject the same request at once: only one may audit it, so only one may be told it ended the session.</summary>
    [Fact]
    public async Task TwoInstancesEndingAtOnce_ExactlyOneIsTold()
    {
        for (var round = 0; round < 25; round++)
        {
            var id = $"race-{round}";
            await NewInstance().StartAsync(Session(id));
            var first = NewInstance();
            var second = NewInstance();

            var results = await Task.WhenAll(
                Task.Run(() => first.EndAsync(id, SessionEndReason.Idle, T0.AddMinutes(1))),
                Task.Run(() => second.EndAsync(id, SessionEndReason.MaxAge, T0.AddMinutes(1))));

            Assert.Single(results, x => x);
        }
    }

    // ---- ending the others

    [Fact]
    public async Task EndingTheOthers_EndsEveryOtherSessionOfTheUser_InAnyTeam_AndCountsThem()
    {
        var store = NewInstance();
        await store.StartAsync(Session("mine", signedInAt: T0.AddHours(3), team: "acme"));
        await store.StartAsync(Session("a", signedInAt: T0, team: "acme"));
        await store.StartAsync(Session("b", signedInAt: T0.AddHours(1), team: "globex"));
        await store.StartAsync(Session("c", signedInAt: T0.AddHours(2)));

        var ended = await store.EndOthersAsync("user-1", "mine", T0.AddHours(4));

        Assert.Equal(3, ended);
        Assert.Null((await store.GetAsync("mine")).EndedAt);
        foreach (var id in new[] { "a", "b", "c" })
        {
            var read = await store.GetAsync(id);
            Assert.Equal((SessionEndReason.Ended, T0.AddHours(4)), (read.EndReason, read.EndedAt));
        }
    }

    [Fact]
    public async Task EndingTheOthers_LeavesOtherUsersAndAlreadyEndedSessionsAlone()
    {
        var store = NewInstance();
        await store.StartAsync(Session("mine"));
        await store.StartAsync(Session("theirs", user: "user-2"));
        await store.StartAsync(Session("gone"));
        await store.EndAsync("gone", SessionEndReason.Idle, T0.AddMinutes(1));

        var ended = await store.EndOthersAsync("user-1", "mine", T0.AddHours(4));

        Assert.Equal(0, ended);
        Assert.Null((await store.GetAsync("theirs")).EndedAt);
        Assert.Equal(SessionEndReason.Idle, (await store.GetAsync("gone")).EndReason);
    }

    // ---- activity

    [Fact]
    public async Task ActivityIsRecorded_WithTheTimeAndTheTeam()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));

        Assert.True(await store.RecordActivityAsync("s1", T0.AddMinutes(2), "acme", TimeSpan.FromMinutes(1)));
        var read = await store.GetAsync("s1");

        Assert.Equal(T0.AddMinutes(2), read.LastActivityAt);
        Assert.Equal("acme", read.ActiveTeamKey);
    }

    [Fact]
    public async Task ActivityWithinTheInterval_IsNotRecorded_AndChangesNothing()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));
        await store.RecordActivityAsync("s1", T0.AddMinutes(2), "acme", TimeSpan.FromMinutes(1));

        Assert.False(await store.RecordActivityAsync("s1", T0.AddMinutes(2).AddSeconds(59), "globex", TimeSpan.FromMinutes(1)));
        var read = await store.GetAsync("s1");

        Assert.Equal(T0.AddMinutes(2), read.LastActivityAt);
        Assert.Equal("acme", read.ActiveTeamKey);
    }

    [Fact]
    public async Task ActivityOnceTheIntervalHasPassed_IsRecordedAgain()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));
        await store.RecordActivityAsync("s1", T0.AddMinutes(2), "acme", TimeSpan.FromMinutes(1));

        Assert.True(await store.RecordActivityAsync("s1", T0.AddMinutes(2).AddSeconds(61), "acme", TimeSpan.FromMinutes(1)));
        Assert.Equal(T0.AddMinutes(2).AddSeconds(61), (await store.GetAsync("s1")).LastActivityAt);
    }

    /// <summary>The throttle is the stored stamp, so it holds when the next request is on another instance: one write per interval for the session, not one per instance.</summary>
    [Fact]
    public async Task TheThrottle_HoldsAcrossInstances()
    {
        var first = NewInstance();
        var second = NewInstance();
        await first.StartAsync(Session("s1"));

        Assert.True(await first.RecordActivityAsync("s1", T0.AddMinutes(2), "acme", TimeSpan.FromMinutes(1)));
        Assert.False(await second.RecordActivityAsync("s1", T0.AddMinutes(2).AddSeconds(10), "acme", TimeSpan.FromMinutes(1)));
        Assert.True(await second.RecordActivityAsync("s1", T0.AddMinutes(3).AddSeconds(5), "acme", TimeSpan.FromMinutes(1)));
        Assert.False(await first.RecordActivityAsync("s1", T0.AddMinutes(3).AddSeconds(30), "acme", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TheThrottle_IsPerSession()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));
        await store.StartAsync(Session("s2"));
        await store.RecordActivityAsync("s1", T0.AddMinutes(2), "acme", TimeSpan.FromMinutes(1));

        Assert.True(await store.RecordActivityAsync("s2", T0.AddMinutes(2), "acme", TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task AZeroInterval_RecordsEveryTime()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));

        Assert.True(await store.RecordActivityAsync("s1", T0.AddMinutes(2), "acme", TimeSpan.Zero));
        Assert.True(await store.RecordActivityAsync("s1", T0.AddMinutes(2).AddSeconds(1), "acme", TimeSpan.Zero));
    }

    /// <summary>A request whose clock is behind another instance's must not move the stamp back and so make the session look idle.</summary>
    [Fact]
    public async Task ActivityOlderThanTheStamp_NeverMovesItBack()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));
        await store.RecordActivityAsync("s1", T0.AddMinutes(10), "acme", TimeSpan.FromMinutes(1));

        Assert.False(await store.RecordActivityAsync("s1", T0.AddMinutes(5), "acme", TimeSpan.FromMinutes(1)));
        Assert.Equal(T0.AddMinutes(10), (await store.GetAsync("s1")).LastActivityAt);
    }

    [Fact]
    public async Task AnEndedSession_RecordsNoActivity()
    {
        var store = NewInstance();
        await store.StartAsync(Session("s1"));
        await store.EndAsync("s1", SessionEndReason.Idle, T0.AddMinutes(1));

        Assert.False(await store.RecordActivityAsync("s1", T0.AddMinutes(30), "acme", TimeSpan.FromMinutes(1)));
        Assert.Equal(T0, (await store.GetAsync("s1")).LastActivityAt);
    }

    [Fact]
    public async Task ActivityOnASessionNobodyStarted_IsNotRecorded()
    {
        Assert.False(await NewInstance().RecordActivityAsync("nobody", T0, "acme", TimeSpan.FromMinutes(1)));
    }

    // ---- entering a team exclusively

    [Fact]
    public async Task TheNewerSignIn_EndsTheOlder_InThatTeam_AndTakesTheTeam()
    {
        var store = NewInstance();
        await store.StartAsync(Session("older", signedInAt: T0, team: "acme"));
        await store.StartAsync(Session("newer", signedInAt: T0.AddHours(1)));

        var entry = await store.EnterTeamExclusiveAsync("newer", "acme", T0.AddHours(2));

        Assert.False(entry.CallerEnded);
        Assert.Equal(["older"], entry.Superseded.Select(x => x.SessionId));
        var older = await store.GetAsync("older");
        Assert.Equal((SessionEndReason.NewerSignIn, T0.AddHours(2)), (older.EndReason, older.EndedAt));
        var newer = await store.GetAsync("newer");
        Assert.Null(newer.EndedAt);
        Assert.Equal("acme", newer.ActiveTeamKey);
    }

    /// <summary>Newest sign-in wins: switching into a team where a newer session already is ends the switching session, not the one that was there.</summary>
    [Fact]
    public async Task EnteringATeam_WhereANewerSessionAlreadyIs_EndsTheCallerInstead()
    {
        var store = NewInstance();
        await store.StartAsync(Session("older", signedInAt: T0));
        await store.StartAsync(Session("newer", signedInAt: T0.AddHours(1), team: "acme"));

        var entry = await store.EnterTeamExclusiveAsync("older", "acme", T0.AddHours(2));

        Assert.True(entry.CallerEnded);
        Assert.Empty(entry.Superseded);
        var older = await store.GetAsync("older");
        Assert.Equal((SessionEndReason.NewerSignIn, T0.AddHours(2)), (older.EndReason, older.EndedAt));
        Assert.Null((await store.GetAsync("newer")).EndedAt);
    }

    [Fact]
    public async Task OnlyTheSameTeam_IsAffected()
    {
        var store = NewInstance();
        await store.StartAsync(Session("elsewhere", signedInAt: T0, team: "globex"));
        await store.StartAsync(Session("noTeam", signedInAt: T0));
        await store.StartAsync(Session("newer", signedInAt: T0.AddHours(1)));

        var entry = await store.EnterTeamExclusiveAsync("newer", "acme", T0.AddHours(2));

        Assert.Empty(entry.Superseded);
        Assert.Null((await store.GetAsync("elsewhere")).EndedAt);
        Assert.Null((await store.GetAsync("noTeam")).EndedAt);
    }

    [Fact]
    public async Task OnlyTheSameUser_IsAffected()
    {
        var store = NewInstance();
        await store.StartAsync(Session("theirs", user: "user-2", signedInAt: T0, team: "acme"));
        await store.StartAsync(Session("mine", user: "user-1", signedInAt: T0.AddHours(1)));

        var entry = await store.EnterTeamExclusiveAsync("mine", "acme", T0.AddHours(2));

        Assert.False(entry.CallerEnded);
        Assert.Empty(entry.Superseded);
        Assert.Null((await store.GetAsync("theirs")).EndedAt);
    }

    [Fact]
    public async Task ASessionThatAlreadyEnded_IsNotEndedAgain_AndDoesNotCountAsNewer()
    {
        var store = NewInstance();
        await store.StartAsync(Session("gone", signedInAt: T0, team: "acme"));
        await store.EndAsync("gone", SessionEndReason.SignedOut, T0.AddMinutes(1));
        await store.StartAsync(Session("endedNewer", signedInAt: T0.AddHours(3), team: "acme"));
        await store.EndAsync("endedNewer", SessionEndReason.SignedOut, T0.AddHours(3));
        await store.StartAsync(Session("mine", signedInAt: T0.AddHours(1)));

        var entry = await store.EnterTeamExclusiveAsync("mine", "acme", T0.AddHours(4));

        Assert.False(entry.CallerEnded);
        Assert.Empty(entry.Superseded);
        var gone = await store.GetAsync("gone");
        Assert.Equal((SessionEndReason.SignedOut, T0.AddMinutes(1)), (gone.EndReason, gone.EndedAt));
    }

    [Fact]
    public async Task EnteringTheSameTeamAgain_EndsNothingMore()
    {
        var store = NewInstance();
        await store.StartAsync(Session("older", signedInAt: T0, team: "acme"));
        await store.StartAsync(Session("newer", signedInAt: T0.AddHours(1)));
        await store.EnterTeamExclusiveAsync("newer", "acme", T0.AddHours(2));

        var again = await store.EnterTeamExclusiveAsync("newer", "acme", T0.AddHours(3));

        Assert.False(again.CallerEnded);
        Assert.Empty(again.Superseded);
    }

    [Fact]
    public async Task EveryOlderSession_InTheTeam_IsEnded()
    {
        var store = NewInstance();
        await store.StartAsync(Session("a", signedInAt: T0, team: "acme"));
        await store.StartAsync(Session("b", signedInAt: T0.AddMinutes(30), team: "acme"));
        await store.StartAsync(Session("c", signedInAt: T0.AddHours(1)));

        var entry = await store.EnterTeamExclusiveAsync("c", "acme", T0.AddHours(2));

        Assert.Equal(["a", "b"], entry.Superseded.Select(x => x.SessionId).Order());
    }

    /// <summary>Both sessions enter on different instances at the same moment: whatever the interleaving, the newer one is the only one left, and the older is ended once.</summary>
    [Fact]
    public async Task TwoInstancesEnteringTheSameTeamAtOnce_ConvergeOnTheNewestSignIn()
    {
        for (var round = 0; round < 25; round++)
        {
            var older = $"older-{round}";
            var newer = $"newer-{round}";
            var seed = NewInstance();
            await seed.StartAsync(Session(older, signedInAt: T0));
            await seed.StartAsync(Session(newer, signedInAt: T0.AddHours(1)));
            var first = NewInstance();
            var second = NewInstance();

            var entries = await Task.WhenAll(
                Task.Run(() => first.EnterTeamExclusiveAsync(older, "acme", T0.AddHours(2))),
                Task.Run(() => second.EnterTeamExclusiveAsync(newer, "acme", T0.AddHours(2))));

            Assert.NotNull((await seed.GetAsync(older)).EndedAt);
            Assert.Null((await seed.GetAsync(newer)).EndedAt);
            Assert.Equal(SessionEndReason.NewerSignIn, (await seed.GetAsync(older)).EndReason);
            // Each end is conditional on the session being un-ended, so the older one is reported as ended by at most one of the two calls.
            Assert.True(entries.Count(x => x.CallerEnded || x.Superseded.Length > 0) >= 1);
            Assert.True(entries.Sum(x => x.Superseded.Length + (x.CallerEnded ? 1 : 0)) <= 2);
        }
    }

    // ---- the collection and the registration

    private static (CreateIndexModel<UserSessionEntity> Index, BsonDocument Keys) Index(UserSessionCollection collection, Func<CreateIndexModel<UserSessionEntity>, bool> pick)
    {
        var index = collection.Indices.Single(pick);
        var keys = index.Keys.Render(new RenderArgs<UserSessionEntity>(BsonSerializer.LookupSerializer<UserSessionEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;
        return (index, keys);
    }

    private static UserSessionCollection Uninitialized() => (UserSessionCollection)RuntimeHelpers.GetUninitializedObject(typeof(UserSessionCollection));

    [Fact]
    public void TheCollection_IsUniqueOnTheSessionId()
    {
        var (index, keys) = Index(Uninitialized(), x => x.Options.Unique == true);

        Assert.Equal(["SessionId"], keys.Names);
    }

    [Fact]
    public void TheCollection_IsIndexedForTheExclusiveTeamQuery()
    {
        var (_, keys) = Index(Uninitialized(), x => x.Options.Unique != true && x.Options.ExpireAfter == null);

        Assert.Equal(["UserKey", "ActiveTeamKey", "EndedAt"], keys.Names);
    }

    /// <summary>Ended sessions expire; a session still going has no <c>EndedAt</c>, which a TTL index leaves alone.</summary>
    [Fact]
    public void TheCollection_ExpiresEndedSessions_OnEndedAt()
    {
        var (index, keys) = Index(Uninitialized(), x => x.Options.ExpireAfter != null);

        Assert.Equal(["EndedAt"], keys.Names);
        Assert.True(index.Options.ExpireAfter > TimeSpan.Zero);
    }

    [Fact]
    public void TheCollection_HasADefaultNameAndAnOption()
    {
        Assert.Equal("UserSession", ThargaTeamOptions.DefaultUserSessionCollectionName);
        Assert.Equal("UserSession", new ThargaTeamOptions().UserSessionCollectionName);
    }

    [Fact]
    public void TheRepository_RegistersTheStore_UnlessTheHostHasItsOwn()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository();

        Assert.Contains(services, x => x.ServiceType == typeof(IUserSessionStore) && x.ImplementationType == typeof(MongoUserSessionStore));
        Assert.Contains(services, x => x.ServiceType == typeof(IUserSessionCollection) && x.ImplementationType == typeof(UserSessionCollection));

        var own = new ServiceCollection();
        var mine = Substitute.For<IUserSessionStore>();
        own.AddSingleton(mine);
        own.AddThargaTeamRepository();

        Assert.Same(mine, Assert.Single(own, x => x.ServiceType == typeof(IUserSessionStore)).ImplementationInstance);
    }
}
