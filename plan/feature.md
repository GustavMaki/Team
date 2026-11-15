# Feature: team-access-workers

A ratchet on the background workers that read the database with no user behind them: each declares `TeamAccess.Unchecked(reason)`, and a test fails the build when a new worker neither declares its access nor is listed as reading no team data. Issue #397 (chosen scope: the CI ratchet, not full enforcement).
