# Plan: team-access-workers

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. Inventory the background workers; classify each as reading the database or not.
- [x] 3. `TeamAccess` moved to `Tharga.Team` (namespace unchanged, forwarded from `Tharga.Team.Service`) so the MongoDB package's start-up checks can declare access.
- [x] 4. Twelve workers declare `TeamAccess.Unchecked(reason)`.
- [x] 5. The ratchet test and a behavioural probe on one worker; docs.
- [~] 6. Push for testing; on the users word close-out, PR, close #397.

## Decisions
- Chosen with the user: a CI ratchet, not full enforcement. The enforcing interceptor stays opt-in and unregistered: it would reject nearly every page, with no known hole behind it.
- The ratchet scans the shipping sources for classes deriving `BackgroundService` or implementing `IHostedService`, as the repo's text-coverage test scans razor. A worker declares access or is listed with a reason; a listed one must not declare, and must still exist, so the list stays true. A mutation check confirmed removing a declaration fails it.
- Declaring `Unchecked` changes nothing at run time with the log-only interceptor; it only records that there is no caller.
- The in-circuit support helpers (no-answer watch, staff presence) read through the decorated services, so they are the central-services case, not workers.

## Release notes
Additive. `TeamAccess`, `TeamAccessKind` and `TeamAccessContext` now live in `Tharga.Team` with the namespace unchanged and type forwards from `Tharga.Team.Service`, so nothing needs recompiling. Background workers declare their access, which a host running the log-only interceptor sees as fewer reported call paths. New documentation of the two interceptors.
