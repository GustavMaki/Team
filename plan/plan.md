# Plan: support-mail-reopens-closed-case

- [x] 1. EmailEventHandler reopens a closed case before appending, with a system entry and a Reopened notification; a not-accepted mail reopens nothing.
- [x] 2. Tests: reopened, order, notification, open case untouched, not-accepted mail; docs.
- [~] 3. Push for testing; on the users word close-out, PR, close #498.

## Release notes
A customer who answers by e-mail after a case closed (by hand, automatically or by a sender rule) now reopens it, with a system entry and a Reopened notification, and the reply counts as awaiting support. Previously the case stayed closed and the reply was filed away.
