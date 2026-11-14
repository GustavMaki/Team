using System.Collections.Concurrent;
using System.Security.Claims;

namespace Tharga.Team.Sample.Framework;

/// <summary>
/// Demonstrates <see cref="ITeamEntryGuard"/>, the host's rule for who may work in a team. Driven by the team's name, so renaming a team tries each answer:
/// a name containing <c>Locked</c> refuses entry with a reason, and one containing <c>BankID</c> sends the caller to <c>/sample-step-up</c> first, which stands
/// in for a stronger sign-in and then sends them back.
/// </summary>
/// <remarks>
/// A real application decides from what the sign-in proved (<c>amr</c>, <c>auth_time</c>) or from its own records, not from a team's name. The step-up here
/// is remembered in memory for the person until the application restarts; a real one would read it from the session.
/// </remarks>
public sealed class SampleTeamEntryGuard(ITeamService teamService) : ITeamEntryGuard
{
    private static readonly ConcurrentDictionary<string, bool> SteppedUp = new(StringComparer.Ordinal);

    public async Task<TeamEntryDecision> CheckAsync(ClaimsPrincipal user, string teamKey, CancellationToken cancellationToken = default)
    {
        var name = (await teamService.GetTeamByKeyAsync(teamKey))?.Name ?? string.Empty;

        if (name.Contains("Locked", StringComparison.OrdinalIgnoreCase))
            return TeamEntryDecision.Deny("This team is locked in the sample. Rename the team without the word Locked to enter it.");

        if (name.Contains("BankID", StringComparison.OrdinalIgnoreCase) && !SteppedUp.ContainsKey(Subject(user)))
            return TeamEntryDecision.StepUp("/sample-step-up?return={returnUrl}", "This team requires a stronger sign-in. Continue to confirm it is you.");

        return TeamEntryDecision.Allow;
    }

    public static void MarkSteppedUp(ClaimsPrincipal user) => SteppedUp[Subject(user)] = true;

    private static string Subject(ClaimsPrincipal user) => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value ?? user?.Identity?.Name ?? string.Empty;
}
