# Plan: support-assistant-elapsed

- [x] 1. NuGet check: nothing new beyond the recorded holds.
- [x] 2. `SupportAssistantProgress.StartedAt`, set by the service on every report; `SupportAssistantUsage.Elapsed`, kept with the answer and mapped in MongoDB.
- [x] 3. Texts: `workingLoopTimed`, `thinkingLoopTimed`, `timed`, `elapsedSeconds`, `elapsedMinutes`; `SupportAssistantSteps` and a ticking `SupportAssistantStatus` used by the transcript and the panel.
- [x] 4. Tests and docs.
- [~] 5. Push for testing; on the users word close-out, PR, close #391.

## Decisions
- The start travels on the progress record, set by the service, so any display counts from the real start; the clock is the display's `TimeProvider`.
- No extra report is made at once: a responder that cannot stream reports nothing, as before, and a streaming one reports as it begins.
- One translatable text with positional placeholders, the time worded by two more texts.
- The final time lives on `SupportAssistantUsage`; an answer with no tokens reported still gets one, and the token count is then not shown.
- A second viewer that receives no progress sees the plain label; only the run that is reported to shows the time.

## Release notes
Additive. `SupportAssistantProgress.StartedAt`, `SupportAssistantUsage.Elapsed`, five new text keys under `team.support.transcript.*`, and the `SupportAssistantStatus` component. The details under an answer also show how long it took.
