# 08: Audit log with a SaveChanges interceptor

## The idea in plain words

A CCTV camera over the till. Staff do not write "I opened the till" in a book (they would
forget, or skip it on purpose); the camera records every opening by itself. In EF Core every
write passes through `SaveChanges`, so an **interceptor** there sees every change to an
audited entity and records it **in the same transaction**: no change without its audit row,
and no audit row for a change that rolled back.

```mermaid
sequenceDiagram
    participant S as ShopProfileService
    participant C as DbContext
    participant I as AuditInterceptor
    participant DB as PostgreSQL
    S->>C: shop.UpdateProfile(...), SaveChanges
    C->>I: SavingChanges
    I->>I: diff Modified fields, mask personal data, drop secrets
    I->>C: Add AuditEntry {who, entity, old -> new, correlationId}
    C->>DB: BEGIN; UPDATE shops; INSERT audit_entries; COMMIT
```

## Why we built it this way

- **Cross-cutting, so not in services.** `IAuditable` on `Shop` and `HishabUser` is all a
  feature needs. A new service cannot forget to audit.
- **Old and new values as stored** (`{"name":{"old":"My shop","new":"Rahim Fashion"}}`):
  Money as a number, enums as text. Unchanged and same-value fields are left out.
- **Privacy by mapping.** `IsPersonalData(Phone|Text)` in the EF configuration masks phones
  (`01712****78`) and replaces addresses with `***`; the entry still shows *that* it changed.
  Hashes, stamps, tokens and concurrency tokens are never written, even without an annotation.
- **Who and which request.** `IRequestContext` (user from the token, correlation ID) is an
  Application interface; the API implements it from `HttpContext`, so Infrastructure never
  touches HTTP. The correlation ID links an entry to the request's log lines.
- **Append-only.** The interceptor refuses to update or delete audit rows. `UserId` has no
  foreign key: removed staff keep their history.
- **Ordering by a database sequence**, not time: timestamps tie and random Guids have no order.
- **Known gap:** `ExecuteUpdate` / `ExecuteDelete` skip the change tracker, so auditable
  entities must be changed through tracked entities.

## Audit log vs event sourcing

An audit log is a side record; the current row stays the source of truth. Event sourcing
makes the events the truth and rebuilds state from them: powerful (replay, time travel) but
it changes how every feature is written. "Who changed the delivery charge?" needs only audit.

## Interview questions

1. **Interceptor or domain events for auditing?** Interceptor: generic, cannot be forgotten,
   sees field-level diffs, but knows *what* changed, not *why*. Domain events: carry intent
   ("StaffPromoted") and business context, but each feature must raise them. Many systems use
   the interceptor for the technical trail and events for business history.
2. **How do you keep the audit log tamper-evident?** Append-only in the app; database role
   without UPDATE/DELETE on the table; a hash chain (each row stores a hash of its content plus
   the previous row's hash) so any edit breaks the chain; ship copies to write-once storage.
3. **What should never go into an audit log?** Secrets (password hashes, tokens, keys) and
   raw personal data. Mask or reference them; the log is often kept longer and read by more
   people than the data itself.
