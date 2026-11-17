# Feature: quieter, named mailbox read logging (#510)

A transient read failure (timeout, dropped connection) is a Warning and an Error only after several in a row; a rejected sign-in backs off from a minute up to an hour; every row names the mailbox (host, user, folder) in its log scope.

State is kept per mailbox in a process-wide MailReadHealth, because the mail client is created per poll. A skipped poll is MailFetchResult.Deferred.
