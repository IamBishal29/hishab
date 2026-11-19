# 0001: Stack and overall architecture

- Status: Accepted
- Date: 2026-11-19

## Context

Hishab is built by one developer, must be free to run, and has to show senior-level
engineering for job applications. The domain (orders, couriers, money) is
transactional and relational. Load is small: hundreds of shops, tens of orders per shop
per day.

## Decision

- **Modular monolith**: one ASP.NET Core Web API (.NET 10 LTS) and one React SPA, split
  internally into modules (Shops, Orders, Customers, Couriers, Money, Products, Reports)
  and into Clean Architecture layers (Domain, Application, Infrastructure, Api).
- **PostgreSQL + EF Core** for data; **Hangfire** (Postgres storage) for background jobs.
- **Plain application services** instead of a mediator library; **Mapperly** for
  mapping; **FluentValidation** for input; **xUnit + Shouldly + NSubstitute** for tests.

## Alternatives considered

| Option | Why not (for now) |
| --- | --- |
| Microservices (one per module) | Distributed transactions, service discovery and several deploys for one developer and tiny load. The module boundaries keep the door open to extract a service later. |
| SQL Server | Free tier (Express) caps database size and is less common on cheap Linux hosting. PostgreSQL is free, runs everywhere, and has good EF Core and Testcontainers support. |
| MediatR (CQRS pipeline) | Moved to a commercial licence. A plain `IOrderService` is easier to read and debug; cross-cutting concerns (validation, logging) can be handled with endpoint filters and decorators. |
| AutoMapper | Commercial licence and runtime reflection. Mapperly generates mapping code at compile time. |

## Consequences

- One deployable and one database: simple CI, local setup and debugging.
- Module boundaries are a discipline, not a network wall; architecture tests and code
  review must keep them honest.
- Hangfire shares the database, so heavy jobs compete with the API for connections;
  fine at this scale, revisit if job volume grows.
