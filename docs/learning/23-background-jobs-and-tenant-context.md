# 23. Background jobs: tenant context outside HTTP, and running twice safely

## The idea in plain words
A shop assistant phones each courier every 15 minutes and asks "where are my parcels?". Before
they open an order book, they say out loud whose book it is. There is no customer at the
counter (no signed-in user) to tell them. If two assistants make the same call by mistake,
the books still come out right: writing "delivered" on an order already marked delivered
changes nothing.

```mermaid
sequenceDiagram
  participant R as Recurring (every 15 min)
  participant D as Dispatcher
  participant Q as Hangfire queue (Postgres)
  participant J as SyncShop(shop, courier)
  participant C as Courier
  R->>D: run
  D->>D: shops with parcels on the road (cross-tenant, IDs only)
  D->>Q: enqueue SyncShop per (shop, courier)
  Q->>J: run (at least once)
  J->>J: new scope, SetTenant(shop) first
  J->>C: status of each parcel (oldest check first)
  J->>J: Decide(order, parcel) -> moves; one SaveChanges per order
```

## Why we built it this way
- **The tenant is a job argument, not something the job works out.** HTTP requests get the
  tenant from the token. A job has no token, so `SyncShop(tenantId, provider)` sets it on a fresh
  DI scope before any query runs, and the usual tenant filter keeps every read and write inside
  that shop. The dispatcher's query is the one deliberate cross-tenant read, and it returns IDs only.
- **A dispatcher, not one recurring job per shop.** Nothing needs registering or removing when
  a courier is connected or disconnected. The job list is derived from the data, so it can't
  drift out of step with it.
- **At-least-once means idempotent.** Hangfire may run a job twice (retry after a crash, or two
  overlapping runs). `ParcelStatusRules.Decide` returns no moves for a status the order has
  already reached. A concurrent change surfaces as `xmin` → `DbUpdateConcurrencyException`, and
  that order is skipped until the next run.
- **Never skip a step, never guess.** "Delivered" seen right after Booked records InTransit and
  then Delivered, so the history stays true to the lifecycle. Unknown statuses are logged with
  the courier's text. A courier-side "Cancelled" and contradictions (returned after delivered)
  are flagged for staff and never forced through.
- **Polling vs webhooks.** Webhooks are faster and cheaper, but they need a public endpoint,
  signature checks, and a fallback for missed calls, which ends up being polling anyway. We
  poll now, oldest check first, at most 200 parcels per run. A webhook can later call the same
  `ApplyAsync`.
- **Operations.** Hangfire state lives in its own `hangfire` schema in our database. `/health`
  reports Degraded when no worker has sent a heartbeat. The dashboard shows every shop's jobs,
  so it is served only in Development, to local requests.

## Interview questions
1. **How does a background job know which tenant it works for?**
   The tenant ID is passed as a job argument when the job is enqueued. The job opens its own DI
   scope and sets the tenant before any data access, so the normal tenant filter applies. It
   never borrows ambient state like `HttpContext` or a static.
2. **What if the job runs twice?**
   Every step is idempotent. A status already applied produces no moves, the shipment and
   order are saved in one transaction, and optimistic concurrency stops two runs from both
   writing. Hangfire guarantees at-least-once, never exactly-once, so you design for repeats.
3. **Polling or webhooks?**
   Webhooks give low latency and less load, but you need a public HTTPS endpoint, signature
   checks, replay protection, and polling anyway to catch missed calls. Starting with bounded
   polling (oldest first, a batch cap, stopping when the courier is down) is the simpler,
   robust base.
