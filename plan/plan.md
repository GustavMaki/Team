# Plan: audit log groups (#402)

Branch: feature/audit-log-group-rows

- [x] 1. Contract: `AuditEntryGroup`, `AuditGroupResult`, grouping rule; default members on `IAuditLogger`, `IAuditReadService`, `IAuditOversightService`
- [x] 2. Store: Mongo pipeline (group by correlation id, empty id per entry), index, `MongoDbAuditLogger`, `CompositeAuditLogger`, `AuditReadService`
- [x] 3. `AuditLogView`: grouped grid with lead row, badge, expandable entries, switch
- [x] 4. Tests (service, mongo pipeline, blazor render), docs
- [~] 5. Full suites, push for testing; on the user's word close-out, PR, close #402

Notes
- The grouping pipeline and the empty-correlation value (taken from the driver, so it carries the host's Guid representation) are not run against a live database: none is available here. The pipeline shape, filters and sort are tested, and the empty value is injected in tests.
- Column sorts other than time are switched off while grouped; the group order is when each unit of work began.
