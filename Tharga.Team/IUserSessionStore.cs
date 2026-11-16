using System.ComponentModel;

namespace Tharga.Team;

/// <summary>
/// Keeps server-side sessions (Tharga/Team#471); internal and unchecked, so a session is started and ended through <see cref="IUserSessionService"/> and
/// the library's own sign-in and cookie check, never by injecting this.
/// </summary>
/// <remarks>
/// <b>Operations, not CRUD: each conditional write is atomic in the store</b>, so two instances acting on one session at once agree. Ending is conditional
/// on the session not being ended, so exactly one caller is told it ended it, and only that one audits; activity is throttled by the stored stamp, so the
/// throttle holds across instances. The store is handed the time and never reads the clock.
/// <c>Tharga.Team.MongoDB</c> registers one; register another before it to keep them elsewhere.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserSessionStore
{
    /// <summary>Stores a new session.</summary>
    Task StartAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>The session, ended or not, or null when there is none by that id.</summary>
    Task<UserSession> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps activity and the active team when at least <paramref name="minInterval"/> has passed since the stored stamp and the session is not ended;
    /// true when it wrote. A time earlier than the stamp never moves it back.
    /// </summary>
    Task<bool> RecordActivityAsync(string sessionId, DateTime at, string teamKey, TimeSpan minInterval, CancellationToken cancellationToken = default);

    /// <summary>Ends the session; true only for the call that ended it. The first end is kept, reason and time.</summary>
    Task<bool> EndAsync(string sessionId, SessionEndReason reason, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every un-ended session of the user but <paramref name="exceptSessionId"/> as <see cref="SessionEndReason.Ended"/>, in any team; returns how many.
    /// It is what <see cref="IUserSessionService.EndOtherSessionsAsync"/> needs.
    /// </summary>
    Task<int> EndOthersAsync(string userKey, string exceptSessionId, DateTime at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the session the only one of its user in the team, newest sign-in winning: the user's older un-ended sessions in the team are ended as
    /// <see cref="SessionEndReason.NewerSignIn"/>, or, when a newer one is already there, the caller's own session is ended instead.
    /// </summary>
    /// <remarks>Each end is conditional on the session not being ended, so two instances entering at once converge on the newest sign-in.</remarks>
    Task<SessionTeamEntry> EnterTeamExclusiveAsync(string sessionId, string teamKey, DateTime at, CancellationToken cancellationToken = default);
}

/// <summary>What entering a team exclusively did.</summary>
/// <param name="Superseded">The user's older sessions in the team that this call ended, as <see cref="SessionEndReason.NewerSignIn"/>.</param>
/// <param name="CallerEnded">True when a newer session was already in the team and this call ended the caller's own session instead.</param>
public sealed record SessionTeamEntry(UserSession[] Superseded, bool CallerEnded);
