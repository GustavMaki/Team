using System.Diagnostics;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Asks the host's <see cref="ITeamEntryGuard"/> whether a caller may work in a team, once per cache window, and records a refusal.
/// </summary>
/// <remarks>
/// <b>Nothing registered, or switched off, is the old behaviour exactly:</b> allowed, with no lookup and no audit. A guard that throws is
/// handled as <see cref="TeamEntryOptions.OnError"/> says. The answer is cached per user, session and team, and a refusal is audited when it is
/// decided, so a refused caller who reloads the page every second leaves one entry per cache window and not one per request.
/// </remarks>
internal sealed class TeamEntryEvaluator(
    TeamEntryCache cache,
    IOptions<ThargaBlazorOptions> options,
    ITeamEntryGuard guard = null,
    ILogger<TeamEntryEvaluator> logger = null,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null)
{
    internal const string ErrorReason = "Entry to this team could not be checked right now, so it is not allowed. Try again shortly.";

    private readonly TeamEntryOptions _options = options.Value.TeamEntry;

    /// <summary>Whether a guard is registered and switched on, which is the only case in which anything here costs anything.</summary>
    public bool Active => guard != null && _options.Enabled;

    public async Task<TeamEntryDecision> EvaluateAsync(ClaimsPrincipal principal, string userKey, string teamKey, CancellationToken cancellationToken = default)
    {
        if (!Active || string.IsNullOrEmpty(teamKey)) return TeamEntryDecision.Allow;

        var user = userKey ?? principal?.Identity?.Name ?? string.Empty;
        var session = TeamEntryCache.SessionOf(principal);
        if (cache.TryGet(user, session, teamKey, out var cached)) return cached;

        var sw = Stopwatch.StartNew();
        var decision = await AskAsync(principal, teamKey, cancellationToken);
        cache.Set(user, session, teamKey, decision, _options.CacheDuration);

        if (!decision.Allowed) Audit(teamKey, decision, sw.ElapsedMilliseconds, null);

        return decision;
    }

    private async Task<TeamEntryDecision> AskAsync(ClaimsPrincipal principal, string teamKey, CancellationToken cancellationToken)
    {
        try
        {
            var decision = await guard.CheckAsync(principal, teamKey, cancellationToken) ?? throw new InvalidOperationException("The team entry guard returned no decision.");
            if (decision.Outcome == TeamEntryOutcome.StepUp && string.IsNullOrWhiteSpace(decision.StepUpUrl))
                throw new InvalidOperationException("The team entry guard asked for a step-up and gave no address to send the caller to.");

            return decision;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The words of the exception stay in the log and the audit entry; the caller is told only that it could not be decided.
            logger?.LogWarning(e, "The team entry guard failed for team {TeamKey}; entry is {Behavior} as configured.", teamKey, _options.OnError);

            if (_options.OnError == TeamEntryErrorBehavior.Allow) return TeamEntryDecision.Allow;

            var denied = TeamEntryDecision.Deny(ErrorReason);
            Audit(teamKey, denied, 0, e.Message);

            return denied;
        }
    }

    private void Audit(string teamKey, TeamEntryDecision decision, long durationMs, string error)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        try
        {
            auditLogger.Log(auditEntryFactory.Create(AuditEventType.AccessLevelDenial, "team", "entry-denied", "CheckAsync", durationMs, false, error ?? decision.Reason, teamKey,
                new Dictionary<string, string>
                {
                    ["team.entry.outcome"] = decision.Outcome.ToString(),
                    ["team.entry.reason"] = decision.Reason ?? string.Empty
                }));
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "A refused team entry could not be audited.");
        }
    }
}
