namespace Tharga.Team;

/// <summary>
/// The host's numbers for how long a session may last in a team: a max age from the sign-in, an idle timeout, and whether a person may have one session
/// in the team at a time (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>The host supplies the numbers; the library does the arithmetic</b>, in one place, on the server, for every request and every live circuit. The
/// answer is cached per session and team for <c>SessionOptions.CacheDuration</c>, so keep it cheap. Limits only restrict: the team is the one the
/// selected-team cookie names, and a person who switches team reloads the page, so the next request is checked against the new team's limits.
/// <para>
/// A policy that throws is handled as <c>SessionOptions.OnError</c> says, allow by default: a database blip must not sign everybody out.
/// </para>
/// Register it as a scoped service. Registering one turns server-side sessions on; with none registered, nothing changes.
/// </remarks>
public interface ISessionPolicy
{
    /// <summary>The limits for <paramref name="session"/> while it works in <paramref name="teamKey"/>.</summary>
    /// <param name="session">The session, with when and how it signed in.</param>
    /// <param name="teamKey">The team the caller is in, or null before one is selected.</param>
    /// <param name="cancellationToken">Cancels the request's check.</param>
    Task<SessionLimits> GetLimitsAsync(UserSession session, string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>The limits a policy gives a session in a team. Every one is optional; <see cref="None"/> is no limit at all.</summary>
public sealed record SessionLimits
{
    /// <summary>How long a session may last from the sign-in, however active; null for no limit.</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>How long a session may go unused; null for no limit.</summary>
    public TimeSpan? IdleTimeout { get; init; }

    /// <summary>Whether a person may have one session in the team at a time. The newest sign-in wins: an older one is ended when a newer one enters.</summary>
    public bool SingleSession { get; init; }

    /// <summary>No limits.</summary>
    public static SessionLimits None { get; } = new();
}
