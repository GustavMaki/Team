using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>A server-side session, one document per sign-in (Tharga/Team#471).</summary>
public record UserSessionEntity : EntityBase
{
    /// <summary>The session's key, as the session claim carries it.</summary>
    public required string SessionId { get; init; }

    /// <summary>The user the session belongs to.</summary>
    public required string UserKey { get; init; }

    /// <summary>The subject the sign-in proved.</summary>
    public string Subject { get; init; }

    /// <summary>When the person signed in, in UTC.</summary>
    public required DateTime SignedInAt { get; init; }

    /// <summary>The identity provider the sign-in went through, when it said.</summary>
    public string IdentityProvider { get; init; }

    /// <summary>How the sign-in was proved, when it said.</summary>
    public string[] AuthenticationMethods { get; init; }

    /// <summary>How the session came to be, stored by name.</summary>
    [BsonRepresentation(BsonType.String)]
    public required SessionOrigin Origin { get; init; }

    /// <summary>When the person was last seen using it, in UTC.</summary>
    public DateTime LastActivityAt { get; init; }

    /// <summary>The team the session was last working in, or null.</summary>
    public string ActiveTeamKey { get; init; }

    /// <summary>When the session ended, in UTC; absent while it lasts.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Why the session ended, stored by name; absent while it lasts.</summary>
    [BsonRepresentation(BsonType.String)]
    public SessionEndReason? EndReason { get; init; }
}

/// <summary>The collection of server-side sessions.</summary>
public interface IUserSessionCollection : IDiskRepositoryCollection<UserSessionEntity>;

/// <summary>Server-side sessions, one document per sign-in, default collection name <c>UserSession</c>; an ended session expires after <see cref="EndedRetention"/>.</summary>
/// <remarks>
/// The unique index is what reads a session by the id its claim carries, on every request whose cache has run out; the second serves entering a team
/// exclusively, which looks for one user's un-ended sessions in one team. A session still going has no <c>EndedAt</c>, which the expiry index leaves alone.
/// </remarks>
public class UserSessionCollection : DiskRepositoryCollectionBase<UserSessionEntity>, IUserSessionCollection
{
    /// <summary>
    /// How long an ended session is kept: long enough for a browser whose cookie still names it to be told why it ended, short enough that the collection
    /// does not grow with every sign-in for good. The audit log, not this, is the record of who signed in and out.
    /// </summary>
    public static readonly TimeSpan EndedRetention = TimeSpan.FromDays(30);

    private readonly string _collectionName;

    /// <summary>Creates the collection under the configured name.</summary>
    public UserSessionCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<UserSessionCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.UserSessionCollectionName ?? ThargaTeamOptions.DefaultUserSessionCollectionName;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<UserSessionEntity>> Indices =>
    [
        new(Builders<UserSessionEntity>.IndexKeys.Ascending(x => x.SessionId),
            new CreateIndexOptions { Unique = true, Name = "SessionId" }),
        new(Builders<UserSessionEntity>.IndexKeys.Ascending(x => x.UserKey).Ascending(x => x.ActiveTeamKey).Ascending(x => x.EndedAt),
            new CreateIndexOptions { Name = "UserKey_ActiveTeamKey_EndedAt" }),
        new(Builders<UserSessionEntity>.IndexKeys.Ascending(x => x.EndedAt),
            new CreateIndexOptions { ExpireAfter = EndedRetention, Name = "EndedAt_ttl" })
    ];
}
