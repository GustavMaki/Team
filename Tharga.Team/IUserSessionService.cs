using System.Security.Claims;

namespace Tharga.Team;

/// <summary>Starts and ends the current caller's server-side session (Tharga/Team#471). Implemented in <c>Tharga.Team.Service</c>.</summary>
/// <remarks>
/// <b>Every operation is the caller's own</b>: none names a session or a user, so none can reach anybody else's. The library's own sign-in and
/// <c>/logout</c> use it; a host that signs people in by other means calls <see cref="StartAsync"/> and signs in the principal it returns.
/// </remarks>
public interface IUserSessionService
{
    /// <summary>
    /// Starts a session for a caller who signed in by other means (origin <see cref="SessionOrigin.Host"/>) and returns the principal with the session claim
    /// stamped, issued by the library. A session claim already on <paramref name="principal"/> is discarded, never believed.
    /// </summary>
    Task<ClaimsPrincipal> StartAsync(ClaimsPrincipal principal);

    /// <summary>Ends the current caller's session as <see cref="SessionEndReason.SignedOut"/>; true when this call ended it, which is when it is audited.</summary>
    Task<bool> EndCurrentAsync();

    /// <summary>Ends every other un-ended session of the current caller, in any team, as <see cref="SessionEndReason.Ended"/>; returns how many.</summary>
    Task<int> EndOtherSessionsAsync();
}
