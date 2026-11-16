namespace Tharga.Team;

/// <summary>
/// One sign-in of one person, kept on the server, so the host's limits on how long it may last can be applied and a session can be ended from the server
/// rather than only by its cookie expiring (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>The browser holds only the id</b>, in the <see cref="TeamClaimTypes.SessionId"/> claim inside the data-protected cookie; everything else is here. It
/// serializes by construction: plain values, no behaviour, so it crosses the store port and the wire as it is.
/// </remarks>
public sealed record UserSession
{
    /// <summary>The session's key: 256 random bits, issued by the library. A secret for as long as the session lasts, so it is never logged or audited.</summary>
    public required string SessionId { get; init; }

    /// <summary>The user the session belongs to (<see cref="IUser.Key"/>).</summary>
    public required string UserKey { get; init; }

    /// <summary>The subject the sign-in proved, as the host's <see cref="UserIdentityResolver"/> reads it; a claim naming the session must come with this subject.</summary>
    public string Subject { get; init; }

    /// <summary>When the person signed in (<c>auth_time</c>), in UTC. A max age counts from here, also for an adopted session.</summary>
    public required DateTime SignedInAt { get; init; }

    /// <summary>The identity provider the sign-in went through (<c>idp</c>), when it said.</summary>
    public string IdentityProvider { get; init; }

    /// <summary>How the sign-in was proved (<c>amr</c>), when it said.</summary>
    public string[] AuthenticationMethods { get; init; }

    /// <summary>How the session came to be.</summary>
    public required SessionOrigin Origin { get; init; }

    /// <summary>When the person was last seen using it, in UTC. An idle timeout counts from here.</summary>
    public DateTime LastActivityAt { get; init; }

    /// <summary>The team the session was last working in, or null.</summary>
    public string ActiveTeamKey { get; init; }

    /// <summary>When the session ended, in UTC; null while it lasts. An ended session never starts again.</summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>Why the session ended; null while it lasts.</summary>
    public SessionEndReason? EndReason { get; init; }
}

/// <summary>How a session came to be.</summary>
public enum SessionOrigin
{
    /// <summary>An interactive sign-in through the library's own sign-in.</summary>
    SignIn,

    /// <summary>A host that signs people in by its own means started it, through <see cref="IUserSessionService.StartAsync"/>.</summary>
    Host,

    /// <summary>A cookie from before sessions existed was given one on its next request, rather than being signed out.</summary>
    Adopted
}

/// <summary>Why a session ended.</summary>
public enum SessionEndReason
{
    /// <summary>The person signed out.</summary>
    SignedOut,

    /// <summary>It lasted longer than the host's <see cref="SessionLimits.MaxAge"/> from the sign-in.</summary>
    MaxAge,

    /// <summary>It was not used for longer than the host's <see cref="SessionLimits.IdleTimeout"/>.</summary>
    Idle,

    /// <summary>The team allows one session per person, and a newer sign-in of theirs entered it.</summary>
    NewerSignIn,

    /// <summary>The person ended it from another of their sessions.</summary>
    Ended
}
