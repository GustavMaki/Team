# Feature: audit log, one row per unit of work (#402)

## Problem
One user action can write many audit rows that share a `CorrelationId` (one domain entry, a proxy trace, and the traces of
the page reloading its dropdowns). `AuditLogView` shows every row, so the meaningful entry is hidden among the traces.

## Decisions (agreed with the user)
- **Grouped in the store.** A grouped query that the database pages by unit of work, so pages and counts are in units of
  work, not entries. The existing filters decide which entries match; a group is the matching entries of one correlation id.
- **Group row = lead entry + count.** The headline is the first `DataChange` of the group, else its first entry; a "+N"
  badge, and failed if any entry of the group failed. Expanding lists every entry of the group in time order, access
  traces last, with the lead's details. A group of one is drawn as a plain row, so background work with no request
  trace looks as it does today, and several entries of one job fold under its own correlation id.
- Entries written before 3.21 have no correlation id (empty). Each of them is a group of its own, never one giant group.
- Groups are ordered by when the unit of work began (its earliest matching entry). Sorting by another column does not
  apply to groups. Export and the charts keep reading entries.
- New members are default members (`IAuditLogger`, `IAuditReadService`, `IAuditOversightService`); a logger that cannot group
  answers a group per entry. A decorator must forward them.
- The view gets a "group by request" switch, on by default, so the flat list is one click away.

## Out of scope
- Grouping the REST/MCP audit reads; the usage and performance tabs.
