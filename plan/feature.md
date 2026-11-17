# Feature: paged team read without members (#403)

## Problem
`TeamComponent.ReloadTeams`, the team selector and `TeamStateService` fetch every team the caller sees **with all its
members**. The selector and the state service then drop the rosters. The cost grows with teams times members, for a
caller in many teams.

## Decisions (agreed with the user)
- Scope of this PR: the contract, the Mongo projection, and the two callers that never use a roster
  (`TeamSelector`, `TeamStateService`). `TeamComponent` and `TeamsListView` keep their full read (they use members in
  about twenty places); a follow-up issue is filed for them.
- Contract, serializable by construction (architecture-v4 rule 3): `TeamPageRequest` (cursor, page size) in,
  `TeamPage` (items, next cursor) out. Items are `TeamSummary` records: key, name, icon and the consent fields,
  no roster. No generic method, no interface return, no `IAsyncEnumerable` on it.
- `TeamSummary` implements `ITeam`, so callers that take an `ITeam` need no change.
- New members are default interface members, so a host's own implementation keeps compiling. Decorators forward them.
- Order is name, then key; the cursor is opaque and names the last row read.
- The `team:read` filter still runs per team in the facade, from the caller's own membership. The store returns only
  the caller's own member entry, which is all the filter needs. A filtered page is topped up so a page is not short
  because rows were left out.
- Member count is not part of the summary; the roster of one team stays one call (`GetTeamByKeyAsync`).

## Out of scope
- `TeamComponent` / `TeamsListView` rows loading their roster on demand (follow-up issue).
- The oversight read (`GetAllTeamsAsync`) used by operators: same shape, later.
