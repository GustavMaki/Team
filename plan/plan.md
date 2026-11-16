# Plan: support-host-owned-assignment

- [x] 1. ShowAssignment (default true) on SupportUnassignedConversation and SupportUnassignedView; the suggestions, dropdown and missing-grant note are left out when false.
- [x] 2. AssignCaseAsync stores TeamName: new ISupportCaseStore.TryAssignCaseAsync overload with a default member delegating to the old one; name from ITeamOversightService, then the callers own teams; best effort.
- [x] 3. Tests (service, render) and docs. No database test for the Mongo write: the repo has none for the store.
- [~] 4. Push for testing; on the users word close-out, PR, close #500.

## Release notes
SupportUnassignedView and SupportUnassignedConversation take ShowAssignment (default true); false leaves out the suggested teams and the team dropdown for a host that assigns in its own control. AssignCaseAsync now stores the teams name in TeamName with TeamKey. ISupportCaseStore gains a TryAssignCaseAsync overload taking the name; it defaults to the old one, so a custom store keeps working and its cases carry no name.
