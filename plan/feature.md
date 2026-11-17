# Feature: support-request-team-access (#379)

Let support staff ask for access to the customer's team from inside a support case, and let the case show what became of the request.

## Decisions (agreed with the user)
- A small optional observer in core (`ITeamAccessRequestObserver`), told by the `TeamManagementService` facade; support registers one that posts the system messages.
- Approving from the case consents all configured consent roles, with the existing approval warning, exactly as the team page does.
- A private case is not read by the team's managers, so they decide it from the team page as before.
- (User) A staff reader who already holds elevated access (member, or consent at Administrator) is shown their current level instead of a request button.

## Scope
- `TeamAccessRequest.SupportCaseId` (+ Mongo), default-member overloads through the service, both decorators, and the facade.
- `SupportCaseAccessRequestObserver` posting System entries for requested / approved (until when) / denied / withdrawn.
- Staff conversation: `SupportCaseTeamAccess` (level info + request button). Customer conversation: `SupportCaseAccessRequests` (the team page's panel filtered to the case).
