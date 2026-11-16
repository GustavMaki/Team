# Feature: support-original-mail (#499)

Keep the original of each received support mail (raw MIME), read it through the service under the same authorization as the
transcript, and offer it in the transcript as an `.eml` download and a raw-text dialog.

## Decisions (agreed with the user)
- The original has its own size cap (`MailOptions.MaxOriginalBytes`, 20 MB), apart from the attachment limits; a mail over it is kept without an original.
- Received mail only. Answers sent by mail are a follow-up.

## Scope
- `IMailClient.FetchAsync(position, includeRaw, ct)` (default member); the support poller asks for the raw message.
- `SupportMessage.OriginalId` / `HasOriginal`; stored in the attachment store, never listed among the message's files.
- `ISupportCaseService.GetMessageOriginalAsync` (default member), authorized as reading the transcript.
- `SupportTranscript`: download as `.eml` and read as raw text, only where an original exists.
