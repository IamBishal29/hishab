# Hishab architecture

Hishab is a **modular monolith**: one ASP.NET Core Web API (.NET 10 LTS) and one React
SPA, backed by PostgreSQL. Decisions behind this are in [`decisions/`](decisions/).

## 1. System context

```mermaid
flowchart LR
    seller([Seller / staff<br/>phone or laptop]) -->|HTTPS| spa[React SPA<br/>Vite + TanStack Query]
    spa -->|REST + JWT| api[Hishab.Api<br/>ASP.NET Core]
    api --> db[(PostgreSQL<br/>app data + Hangfire)]
    jobs[Hangfire workers<br/>status sync, COD alerts] --> db
    api -.hosts.-> jobs
    api -->|ICourierProvider| couriers[Courier APIs<br/>Fake · Pathao · Steadfast]
    jobs -->|poll status| couriers
    api -->|Microsoft.Extensions.AI| gemini[Gemini<br/>chat-to-order, V2]
    api -->|structured logs| seq[Seq<br/>local log viewer]
```

- The SPA never talks to couriers or Gemini directly; the API owns every external call
  so keys stay on the server.
- Hangfire runs in the API process (one deployable) and stores jobs in the same
  PostgreSQL database.
- Courier calls use the seller's own API keys, through official APIs only.

## 2. Layers and modules

```mermaid
flowchart TB
    subgraph Api[Hishab.Api]
        ctl[Controllers · ProblemDetails handler · auth policies · OpenAPI]
    end
    subgraph Infra[Hishab.Infrastructure]
        ef[EF Core DbContext · tenant filters · migrations]
        cour[Courier adapters · Hangfire jobs · Gemini client]
    end
    subgraph App[Hishab.Application]
        svc[Use-case services · DTOs · validators · interfaces]
    end
    subgraph Dom[Hishab.Domain]
        ent[Entities · value objects Money/Phone · order state machine]
    end
    Api --> App
    Api --> Infra
    Infra --> App
    App --> Dom
```

| Rule | Enforced by |
| --- | --- |
| Domain references nothing | `Hishab.ArchitectureTests` (NetArchTest) |
| Application references Domain only | `Hishab.ArchitectureTests` |
| Infrastructure implements Application interfaces | Dependency injection in `Hishab.Api` |

Each layer has a feature folder per module: **Shops, Orders, Customers, Couriers, Money,
Products, Reports**. A module only reaches into another one through its Application
services, never through its tables.

## 3. Tenant isolation

Every tenant-owned row has a `TenantId`. The tenant comes from the signed JWT, never
from the request body or URL.

```mermaid
sequenceDiagram
    participant SPA
    participant Api as Api (JWT middleware)
    participant Ctx as ITenantContext
    participant Db as HishabDbContext
    participant PG as PostgreSQL
    SPA->>Api: GET /orders  (Authorization: Bearer ...)
    Api->>Api: validate signature, read "tenant_id" claim
    Api->>Ctx: TenantId = claim value
    Api->>Db: _db.Orders.ToListAsync()
    Db->>Db: global query filter: o.TenantId == ctx.TenantId
    Db->>PG: SELECT ... WHERE tenant_id = @tenant
    Note over Db: SaveChanges stamps TenantId on new rows<br/>and rejects writes for another tenant
```

Failure mode this protects against: a missing `WHERE` clause leaking Shop A's customers
to Shop B. Every new entity gets a cross-tenant integration test.

## 4. Order state machine

```mermaid
stateDiagram-v2
    [*] --> New: seller enters
    New --> Confirmed: risk checked
    New --> Cancelled
    Confirmed --> Cancelled: fake or unreachable
    Confirmed --> Booked: courier API
    Booked --> InTransit: auto-synced
    InTransit --> Delivered: cash collected
    InTransit --> Returned: buyer refuses
    Delivered --> CodSettled: payout matched
    Returned --> ReceivedBack: stock back, charges logged
    CodSettled --> [*]
    ReceivedBack --> [*]
    Cancelled --> [*]
```

The transitions live in the Domain (`Order` aggregate), not in controllers. An invalid
transition throws a domain exception, which the global handler maps to a 409
ProblemDetails. Every transition appends an `OrderStatusHistory` row (who, when, from, to).

## 5. Repository layout

```text
src/api/      Hishab.sln: Domain, Application, Infrastructure, Api
src/web/      React + TypeScript + Vite SPA
tests/        UnitTests, IntegrationTests (Testcontainers), ArchitectureTests
docs/         PRD, this file, ADRs (decisions/), learning notes (learning/)
.github/      CI workflow, PR template
```
