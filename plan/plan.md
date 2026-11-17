# Plan: paged team read (#403)

Branch: feature/paged-team-read

- [x] 1. Contract: `TeamSummary`, `TeamPageRequest`, `TeamPage`; default members on `ITeamDirectoryService` and `ITeamService`
- [x] 2. `TeamServiceBase` + Mongo: projection returning only the caller's own member, cursor by name and key
- [x] 3. Facade (`TeamManagementService`): `team:read` filter with top-up; decorators forward (Authorization, Auditing)
- [x] 4. `TeamSelector` and `TeamStateService` read summaries
- [x] 5. Tests (service, mongo, blazor, decorator default members), docs
- [~] 6. Full suites, push for testing; on the user's word close-out, PR, close #403, file the follow-up issue

Notes
- Added a non-generic `ITeamService.GetTeamPageAsync` as well, because `TeamStateService` does not know the host's member type.
- The Mongo projection is `$elemMatch` on Members only. Not run against a live database (none available here); the rendered filter, sort and projection are tested.
- `TeamSummary.cs` is allowed in the consent-read architecture test (it carries stored consent and its temporary part unread).
- The selector still reads every page (client-side filter); the saving is the rosters. Loading pages as the user scrolls is a follow-up.
