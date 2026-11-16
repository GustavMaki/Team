# Plan: support-original-mail

- [x] 1. Mail client: includeRaw overload as a default member; poller fetches with the raw message.
- [x] 2. Handler keeps the original per message (own cap, never loses the mail); `SupportMessage.OriginalId`/`HasOriginal`; Mongo mapping.
- [x] 3. `GetMessageOriginalAsync` in the service and both decorators.
- [x] 4. Transcript: download `.eml`, read as raw text in a dialog (cut at 200,000 characters).
- [x] 5. Tests (handler, service authorization, transcript) and docs.
- [~] 6. Full suites, push for testing; on the users word close-out, PR, close #499.

## Release notes
A support mail is now kept as it arrived. Staff can download it as an .eml or read it as raw text from the transcript. Mail received earlier has none.
