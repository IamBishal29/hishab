# 0002: Multi-tenancy with a shared database and a TenantId column

- Status: Accepted
- Date: 2026-11-19

## Context

Every shop (tenant) must only ever see its own orders, customers and money. We expect
many small tenants (most with fewer than 1,000 orders a month) and one small server.

## Decision

- One shared PostgreSQL database and schema. Every tenant-owned table has a
  non-nullable `TenantId` column, indexed (usually as the first column of composite
  indexes).
- The tenant is read from the `tenant_id` claim of the authenticated user's JWT into a
  scoped `ITenantContext`. It is never accepted from the request body, query or URL.
- `HishabDbContext` applies an EF Core **global query filter** on every entity that
  implements `ITenantOwned`, and `SaveChanges` stamps `TenantId` on new rows and refuses
  changes to rows of another tenant.
- Every new tenant-owned entity gets a cross-tenant integration test (Tenant A's data is
  invisible to Tenant B).

## Alternatives considered

| Option | Trade-off |
| --- | --- |
| Database per tenant | Strongest isolation, but hundreds of databases to migrate and back up, and connection pools per tenant. Too heavy for tiny tenants. |
| Schema per tenant | Better isolation than a column, but EF Core model caching per schema and migrations across many schemas add complexity. |
| PostgreSQL row-level security (RLS) | Database-enforced and a good second layer. Needs the tenant set on each connection (`SET app.tenant_id`), which is tricky with pooling. Can be added later on top of this design. |

## Consequences

- Cheap to run and simple to migrate: one schema, one migration run.
- A bug that bypasses the filter (`IgnoreQueryFilters`, raw SQL) could leak data, so
  those calls are banned outside Infrastructure and reviewed carefully.
- Jobs without a user (Hangfire) must set the tenant explicitly for each unit of work.
- The shared risk network (V2) is deliberately cross-tenant and stores only hashed phone
  numbers in its own, non-tenant table.
