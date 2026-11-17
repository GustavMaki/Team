namespace Tharga.Team;

/// <summary>
/// Told when a request for access to a team is made, approved, denied or withdrawn, so something outside the team service can react to it — the
/// support case a request was made from showing it, for one.
/// </summary>
/// <remarks>
/// <b>Called by <see cref="TeamManagementService{TMember}"/>, after the change has been made and authorized.</b> That is the service every component
/// and controller injects, so a decision made on the team page reaches an observer exactly as one made anywhere else. A host that injects the
/// inner <c>ITeamService</c> directly bypasses it.
/// <para>
/// <b>An observer that throws never undoes or hides the change.</b> It is logged, and the request stands as it was decided.
/// </para>
/// </remarks>
public interface ITeamAccessRequestObserver
{
    /// <summary>A request changed: it was made (<see cref="TeamAccessRequestStatus.Pending"/>), or decided or withdrawn, as its <see cref="TeamAccessRequest.Status"/> says.</summary>
    Task ChangedAsync(string teamKey, TeamAccessRequest request, CancellationToken cancellationToken = default);
}
