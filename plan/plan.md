# Plan: support-request-team-access

- [x] 1. Core: `SupportCaseId`, 5-argument overloads (default members), Authorization/Auditing decorators, facade observers, Mongo entity.
- [x] 2. Support: observer posting the System entries; registered with the support package.
- [x] 3. Blazor: staff block (current level, request), approval card for the team's managers; dialog/button/panel take the case.
- [x] 4. Tests (core, facade, decorators, observer, Mongo mapping, render) and docs (access-requests.md).
- [~] 5. Full suites, push for testing; on the users word close-out, PR, close #379.

## Release notes
Support staff can ask for access to the customer's team from a support case, which shows the request and its outcome, and the team's managers can decide it there.
