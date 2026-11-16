# Plan: clean-up-warnings

- [x] 1. Re-measured: 67 compiler/analyzer warnings (the issues numbers were three months old). No ReSharper CLI here, so its findings were located by hand.
- [x] 2. XML docs fixed (CS1573, CS1574, CS0419, CS1587); the two CS4014 in the Mongo tests, CS0108, CS9113 and xUnit1031 in tests.
- [x] 3. The async event handlers that nothing awaits are guarded (TeamSelector x3, SuspendedTeamNotice, TeamStateService); UserProfileView null reference; three redundant StateHasChanged removed.
- [~] 4. Decide with the user: the Blazor analyzer warnings BL0013 (19), BL0016 (19), BL0012 (4 left) that the issue did not list.
- [ ] 5. Close-out, PR, close #406.

## Release notes
No behaviour change to a working circuit. Event handlers that nothing awaits now log a failure instead of ending the circuit.
