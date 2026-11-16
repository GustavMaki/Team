using System.Security.Claims;
using Tharga.Team;

namespace Tharga.Team.Service.Audit;

/// <summary>
/// Audit entries for the events that happen outside any service call: an interactive sign-in, the user record
/// created as a side effect of a first one, and the server-side session's sign-out and ends (Tharga/Team#471).
/// </summary>
/// <remarks>
/// <b>Why these were missing.</b> Every other audited action passes through a service the auditing decorators
/// wrap. These two do not: a sign-in completes inside the authentication handshake, and a first-sign-in user
/// record is created while resolving the caller. So the audit log could say what someone did but never that
/// they arrived — and <c>Tharga.Team.Support</c> could not route the two events Tharga/Team#142 names first,
/// "user logs on" and "user created", because neither existed to route.
/// <para>
/// Built here rather than at the call sites so the shape is defined once and both are testable without an
/// authentication pipeline.
/// </para>
/// </remarks>
public static class AuthAuditEntries
{
    /// <summary>Feature name both entries carry, matching the API-key handler's <c>auth</c>.</summary>
    public const string Feature = "auth";

    public const string SignInAction = "signin";
    public const string UserCreatedAction = "user-created";
    public const string UserProfileRefreshedAction = "user-profile-refreshed";
    public const string SignOutAction = "signout";
    public const string SessionEndedAction = "session-ended";
    public const string SessionSupersededAction = "session-superseded";

    /// <summary>The metadata key the reason a session ended is recorded under, by name (<see cref="SessionEndReason"/>).</summary>
    public const string SessionReasonKey = "session.reason";

    /// <summary>A person signing out, which ended their server-side session (Tharga/Team#471).</summary>
    /// <remarks>
    /// Audited only when sessions are on, and only by the call that ended the session, so two tabs signing out at once leave one entry. Like
    /// <see cref="SignIn(ClaimsPrincipal)"/> it names no team, and like every session entry it never names the session: its id is a key.
    /// </remarks>
    public static AuditEntry SignOut(ClaimsPrincipal principal)
        => SignOut(principal, UserIdentityResolver.Default);

    /// <summary>A sign-out, with the subject read by the host's identity resolver.</summary>
    public static AuditEntry SignOut(ClaimsPrincipal principal, UserIdentityResolver identityResolver)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.AuthSuccess,
            Feature = Feature,
            Action = SignOutAction,
            MethodName = nameof(SignOut),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = Identity(principal),
            CallerUserIdentity = (identityResolver ?? UserIdentityResolver.Default).GetSubject(principal)
        };

    /// <summary>A person's server-side session ending other than by their signing out: a limit of the host's, or a newer sign-in taking the team.</summary>
    /// <remarks>
    /// Audited once, by the request that ended it, however many requests the ended session goes on to make; the reason is in the metadata under
    /// <see cref="SessionReasonKey"/>. Recorded as an auth event that happened rather than as a failure: nobody's credentials failed.
    /// </remarks>
    public static AuditEntry SessionEnded(ClaimsPrincipal principal, SessionEndReason reason)
        => SessionEnded(principal, reason, UserIdentityResolver.Default);

    /// <summary>A session ending, with the subject read by the host's identity resolver.</summary>
    public static AuditEntry SessionEnded(ClaimsPrincipal principal, SessionEndReason reason, UserIdentityResolver identityResolver)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.AuthSuccess,
            Feature = Feature,
            Action = SessionEndedAction,
            MethodName = nameof(SessionEnded),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = Identity(principal),
            CallerUserIdentity = (identityResolver ?? UserIdentityResolver.Default).GetSubject(principal),
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [SessionReasonKey] = reason.ToString() }
        };

    /// <summary>An older session of a person ended because a newer sign-in of theirs entered a team that allows one session at a time.</summary>
    /// <remarks>
    /// Written by the newer session's request, so there is no principal for the older one: the person is named by the subject and user key the session
    /// was started with, and the team is the one it was in.
    /// </remarks>
    public static AuditEntry SessionSuperseded(UserSession superseded)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.AuthSuccess,
            Feature = Feature,
            Action = SessionSupersededAction,
            MethodName = nameof(SessionSuperseded),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerUserIdentity = superseded?.Subject,
            TeamKey = superseded?.ActiveTeamKey,
            Metadata = Superseded(superseded)
        };

    private static Dictionary<string, string> Superseded(UserSession session)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [SessionReasonKey] = nameof(SessionEndReason.NewerSignIn) };
        if (!string.IsNullOrEmpty(session?.UserKey)) metadata["user.key"] = session.UserKey;

        return metadata;
    }

    /// <summary>An interactive sign-in completing.</summary>
    /// <remarks>
    /// <see cref="AuditCallerType.User"/> and <see cref="AuditCallerSource.Web"/> — the counterpart to the
    /// API-key handler's <c>auth/apikey</c> entry, which is <see cref="AuditCallerType.ApiKey"/>.
    /// No team: sign-in precedes team selection, so naming one here would be an invention.
    /// </remarks>
    public static AuditEntry SignIn(ClaimsPrincipal principal)
        => SignIn(principal, UserIdentityResolver.Default);

    /// <summary>An interactive sign-in completing, with the subject read by the host's identity resolver.</summary>
    public static AuditEntry SignIn(ClaimsPrincipal principal, UserIdentityResolver identityResolver)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.AuthSuccess,
            Feature = Feature,
            Action = SignInAction,
            MethodName = nameof(SignIn),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = Identity(principal),
            CallerUserIdentity = (identityResolver ?? UserIdentityResolver.Default).GetSubject(principal)
        };

    /// <summary>
    /// A user record created because someone signed in for the first time.
    /// </summary>
    /// <remarks>
    /// <see cref="AuditEventType.DataChange"/> rather than an auth event: the sign-in is reported separately,
    /// and this is a write. The actor is the new user themselves — nobody else asked for it, which is what
    /// distinguishes it from an administrator creating a user through <c>IUserManagementService</c>.
    /// </remarks>
    public static AuditEntry UserCreated(IUser user, ClaimsPrincipal principal)
        => UserCreated(user, principal, UserIdentityResolver.Default);

    /// <summary>A first-sign-in user record, with the subject read by the host's identity resolver.</summary>
    public static AuditEntry UserCreated(IUser user, ClaimsPrincipal principal, UserIdentityResolver identityResolver)
        => new()
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataChange,
            Feature = Feature,
            Action = UserCreatedAction,
            MethodName = nameof(UserCreated),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = user?.Identity ?? Identity(principal),
            CallerUserIdentity = (identityResolver ?? UserIdentityResolver.Default).GetSubject(principal),
            Metadata = Describe(user)
        };

    /// <summary>
    /// A stored user's name and/or email refreshed from a sign-in the host trusts
    /// (<c>UserServiceBase.ShouldRefreshProfile</c>). <paramref name="name"/> or <paramref name="email"/> is null
    /// when that value was left unchanged.
    /// </summary>
    /// <remarks>
    /// <see cref="AuditEventType.DataChange"/> with the user as their own actor, like <see cref="UserCreated(IUser, ClaimsPrincipal)"/>.
    /// The metadata names which values changed and the address the user now has; the name itself is not copied
    /// into the log.
    /// </remarks>
    public static AuditEntry UserProfileRefreshed(IUser user, string name, string email, ClaimsPrincipal principal)
        => UserProfileRefreshed(user, name, email, principal, UserIdentityResolver.Default);

    /// <summary>A refreshed profile, with the subject read by the host's identity resolver.</summary>
    public static AuditEntry UserProfileRefreshed(IUser user, string name, string email, ClaimsPrincipal principal, UserIdentityResolver identityResolver)
    {
        var metadata = Describe(user) ?? new Dictionary<string, string>(StringComparer.Ordinal);
        metadata["changed"] = string.Join(",", new[] { name != null ? "name" : null, email != null ? "email" : null }.Where(x => x != null));
        if (email != null) metadata["user.email"] = email;

        return new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = AuditEventType.DataChange,
            Feature = Feature,
            Action = UserProfileRefreshedAction,
            MethodName = nameof(UserProfileRefreshed),
            Success = true,
            CallerType = AuditCallerType.User,
            CallerSource = AuditCallerSource.Web,
            CallerIdentity = user?.Identity ?? Identity(principal),
            CallerUserIdentity = (identityResolver ?? UserIdentityResolver.Default).GetSubject(principal),
            Metadata = metadata
        };
    }

    private static string Identity(ClaimsPrincipal principal)
        => principal?.FindFirst(ClaimTypes.Email)?.Value
           ?? principal?.FindFirst(ClaimTypes.Upn)?.Value
           ?? principal?.Identity?.Name;

    private static Dictionary<string, string> Describe(IUser user)
    {
        if (user == null) return null;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(user.Key)) metadata["user.key"] = user.Key;
        if (!string.IsNullOrEmpty(user.EMail)) metadata["user.email"] = user.EMail;

        return metadata.Count > 0 ? metadata : null;
    }
}
