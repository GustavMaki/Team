# Plan: adopt-tharga-blazor-2-4

- [x] 1. Blazored.LocalStorage referenced directly and registered by AddThargaTeamBlazor; registration test.
- [~] 2. User looks at the date pickers (4) and audit charts (6) in the sample site and decides keep new defaults or pin the old.
- [ ] 3. Close-out, PR, close #396.

## Release notes
Tharga.Team.Blazor now references Blazored.LocalStorage itself and registers ILocalStorageService, since Tharga.Blazor 2.4 deprecates providing it.
