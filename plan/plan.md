# Plan: team-entry-guard

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. Contracts in `Tharga.Team`: `ITeamEntryGuard`, `TeamEntryDecision`, `ITeamEntryGuardState`; options `TeamEntry` (Enabled, OnError, CacheDuration).
- [x] 3. Enforcement in `TeamMembershipClaimsBuilder` through `TeamEntryEvaluator` + `TeamEntryCache`; audit of refusals; fail mode.
- [x] 4. UI: `ITeamEntryService`, `TeamStateService`, selector badge, `TeamEntryNotice`; step-up navigation.
- [x] 5. Invalidation: open circuits of the user on this instance re-evaluate at once.
- [x] 6. Tests, docs, sample demo guard.
- [~] 7. Push for testing; on the users word close-out, PR, close #472.

## Decisions
- Opt-in by registration; `Enabled` is a kill switch; with no guard nothing runs but a null check.
- Fail closed by default (`OnError = Deny`), configurable; the exception text never reaches the caller.
- Cache per user, session (`sid`, else `auth_time`/`idp`/`amr`) and team, 30 s, per instance; invalidation clears and notifies circuits.
- The guard returns a step-up address with a `{returnUrl}` placeholder, filled at redirect time from the current page, so a cached decision never embeds one page. The host's endpoint must still allow local paths only.
- The refused team's marker claim stays on the principal so the revalidator can give the team back after a step-up; only the TeamKey claim and scopes are withheld.
- No fallback to another team for a refused one: the caller has no selection and the reason, and can pick another.
- The guard is asked only after grant resolution says the caller is allowed in at all.
- Audit event type is `AccessLevelDenial`, feature `team`, action `entry-denied`.

## Release notes
Additive. New `ITeamEntryGuard` / `TeamEntryDecision` / `ITeamEntryGuardState` (Tharga.Team), `o.Blazor.TeamEntry`, `ITeamEntryService`, `TeamEntryNotice` and the *Not allowed* badge in the selector. Register a guard to use it; with none, team entry is unchanged.
