using MongoDB.Bson;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IUserSessionStore"/>: one document per session, every write conditional in its filter, so it is atomic per document.</summary>
/// <remarks>
/// <b>The conditions are what make the store safe across instances</b>, not anything held in this process: an end matches only a session with no
/// <c>EndedAt</c>, so exactly one caller sees it change; activity matches only a stamp old enough, so the throttle is the stored one. Entering a team
/// exclusively spans several documents and is not one transaction; it converges instead, because each session takes the team before it looks for others
/// there, and every end it makes is conditional (see <see cref="EnterTeamExclusiveAsync"/>).
/// </remarks>
internal sealed class MongoUserSessionStore(IUserSessionCollection collection) : IUserSessionStore
{
    private static FilterDefinitionBuilder<UserSessionEntity> Filter => Builders<UserSessionEntity>.Filter;

    private static FilterDefinition<UserSessionEntity> Live => Filter.Eq(x => x.EndedAt, null);

    public Task StartAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        return collection.AddAsync(ToEntity(session));
    }

    public async Task<UserSession> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;

        var entity = await collection.GetOneAsync(Of(sessionId), cancellationToken: cancellationToken);
        return entity == null ? null : ToSession(entity);
    }

    /// <remarks>One conditional update: the session is live and its stamp is at least <paramref name="minInterval"/> older than <paramref name="at"/>.</remarks>
    public async Task<bool> RecordActivityAsync(string sessionId, DateTime at, string teamKey, TimeSpan minInterval, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;

        var due = Filter.Lte(x => x.LastActivityAt, at - (minInterval < TimeSpan.Zero ? TimeSpan.Zero : minInterval));
        var update = Builders<UserSessionEntity>.Update.Set(x => x.LastActivityAt, at).Set(x => x.ActiveTeamKey, teamKey);

        return (await collection.UpdateOneAsync(Of(sessionId) & Live & due, update, OneOption<UserSessionEntity>.FirstOrDefault))?.Before != null;
    }

    public async Task<bool> EndAsync(string sessionId, SessionEndReason reason, DateTime at, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;

        return (await collection.UpdateOneAsync(Of(sessionId) & Live, Ending(reason, at), OneOption<UserSessionEntity>.FirstOrDefault))?.Before != null;
    }

    public async Task<int> EndOthersAsync(string userKey, string exceptSessionId, DateTime at, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userKey)) return 0;

        var others = Filter.Eq(x => x.UserKey, userKey) & Filter.Ne(x => x.SessionId, exceptSessionId) & Live;
        return (int)await collection.UpdateManyAsync(others, Ending(SessionEndReason.Ended, at));
    }

    /// <remarks>
    /// <b>Not one transaction, but it converges.</b> The caller takes the team first and only then looks for the user's other live sessions there, so of two
    /// sessions entering at once on two instances, whichever looks second sees the first. The older of the two is then ended, by itself or by the newer,
    /// and since each end is conditional on the session being live, it is ended once and reported once. Newer means signed in strictly later: on a tie
    /// the session entering wins, as a later sign-in would.
    /// </remarks>
    public async Task<SessionTeamEntry> EnterTeamExclusiveAsync(string sessionId, string teamKey, DateTime at, CancellationToken cancellationToken = default)
    {
        var caller = await collection.GetOneAsync(Of(sessionId), cancellationToken: cancellationToken);
        if (caller == null || caller.EndedAt != null) return new SessionTeamEntry([], false);

        await collection.UpdateOneAsync(Of(sessionId) & Live, Builders<UserSessionEntity>.Update.Set(x => x.ActiveTeamKey, teamKey), OneOption<UserSessionEntity>.FirstOrDefault);

        var inTeam = new List<UserSessionEntity>();
        await foreach (var other in collection.GetAsync(Filter.Eq(x => x.UserKey, caller.UserKey) & Filter.Eq(x => x.ActiveTeamKey, teamKey) & Live & Filter.Ne(x => x.SessionId, sessionId), cancellationToken: cancellationToken))
        {
            inTeam.Add(other);
        }

        if (inTeam.Any(x => x.SignedInAt > caller.SignedInAt)) return new SessionTeamEntry([], await EndAsync(sessionId, SessionEndReason.NewerSignIn, at, cancellationToken));

        var superseded = new List<UserSession>();
        foreach (var older in inTeam)
        {
            if (await EndAsync(older.SessionId, SessionEndReason.NewerSignIn, at, cancellationToken)) superseded.Add(ToSession(older) with { EndedAt = at, EndReason = SessionEndReason.NewerSignIn });
        }

        return new SessionTeamEntry([.. superseded], false);
    }

    private static FilterDefinition<UserSessionEntity> Of(string sessionId) => Filter.Eq(x => x.SessionId, sessionId);

    private static UpdateDefinition<UserSessionEntity> Ending(SessionEndReason reason, DateTime at)
        => Builders<UserSessionEntity>.Update.Set(x => x.EndedAt, at).Set(x => x.EndReason, reason);

    internal static UserSessionEntity ToEntity(UserSession session) => new()
    {
        Id = ObjectId.GenerateNewId(),
        SessionId = session.SessionId ?? throw new ArgumentException("A session has an id.", nameof(session)),
        UserKey = session.UserKey,
        Subject = session.Subject,
        SignedInAt = session.SignedInAt,
        IdentityProvider = session.IdentityProvider,
        AuthenticationMethods = session.AuthenticationMethods,
        Origin = session.Origin,
        LastActivityAt = session.LastActivityAt,
        ActiveTeamKey = session.ActiveTeamKey,
        EndedAt = session.EndedAt,
        EndReason = session.EndReason
    };

    internal static UserSession ToSession(UserSessionEntity entity) => new()
    {
        SessionId = entity.SessionId,
        UserKey = entity.UserKey,
        Subject = entity.Subject,
        SignedInAt = DateTime.SpecifyKind(entity.SignedInAt, DateTimeKind.Utc),
        IdentityProvider = entity.IdentityProvider,
        AuthenticationMethods = entity.AuthenticationMethods,
        Origin = entity.Origin,
        LastActivityAt = DateTime.SpecifyKind(entity.LastActivityAt, DateTimeKind.Utc),
        ActiveTeamKey = entity.ActiveTeamKey,
        EndedAt = entity.EndedAt == null ? null : DateTime.SpecifyKind(entity.EndedAt.Value, DateTimeKind.Utc),
        EndReason = entity.EndReason
    };
}
