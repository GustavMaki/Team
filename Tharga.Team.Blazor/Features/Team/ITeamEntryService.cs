using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// What a signed-in circuit knows about being refused entry to a team by the host's <see cref="ITeamEntryGuard"/>: the refusal to show, and how to
/// send the caller to step up.
/// </summary>
/// <remarks>
/// For the team selector and <c>TeamEntryNotice</c>; a host rarely needs it. Enforcement does not depend on it: the server does not issue a refused team's
/// claims whatever the browser shows.
/// </remarks>
public interface ITeamEntryService
{
    /// <summary>The team this circuit asked for and was refused, or null when nothing is refused.</summary>
    TeamEntryRefusal Refusal { get; }

    /// <summary>Raised when <see cref="Refusal"/> changes: a team was refused, or the refusal was cleared by an allowed entry.</summary>
    event EventHandler<TeamEntryRefusal> RefusalChanged;

    /// <summary>Asks whether the caller may work in <paramref name="teamKey"/> now; always allowed when no guard is registered or it is switched off.</summary>
    Task<TeamEntryDecision> CheckAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Sends the browser to the step-up address of <paramref name="refusal"/>, with <c>{returnUrl}</c> filled in with the page it is on; does nothing for a refusal that has none.</summary>
    void StepUp(TeamEntryRefusal refusal);
}

/// <summary>A team the caller was refused entry to, and what the guard said.</summary>
/// <param name="TeamKey">The team.</param>
/// <param name="Decision">The guard's answer: a deny with a reason, or a step-up.</param>
public sealed record TeamEntryRefusal(string TeamKey, TeamEntryDecision Decision);
