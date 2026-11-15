# Feature: localised dates and audit texts

## Goal
Text and moments a host cannot translate or format today follow the host instead.

## Scope
- The audit log's retention sentence and its page-size label go through the view's text catalogue.
- The team grid's page-size label goes through its catalogue.
- The contact table writes when consent was given through the host's `TimeTemplate`, like every other moment on the support boards.

## Acceptance criteria
- A host translating `team.auditLogView.retentionDays`, `team.auditLogView.retentionIndefinite`, `team.auditLogView.pageSize` and `team.component.pageSize` sees its text; untranslated, the English is unchanged.
- With a `TimeTemplate` set on a support board, the consent row renders through it; without one it reads as before.

## Done condition
Tests green, PR merged, patch release published.
