# 0003: Controllers for the HTTP layer

- Status: Accepted
- Date: 2026-11-19

## Context

ASP.NET Core offers two ways to expose endpoints: MVC controllers and minimal APIs.
Both are supported in .NET 10 and produce the same OpenAPI document.

## Decision

Use **controllers** (`[ApiController]`), one per module resource, kept thin: bind a request
DTO, call one Application service, return the result. No business rules in controllers.

## Alternatives considered

| Option | Trade-off |
| --- | --- |
| Minimal APIs with endpoint groups | Less ceremony and slightly faster startup; filters and conventions are less familiar to many .NET reviewers and teams. |

## Consequences

- Familiar structure for any .NET reviewer; attribute routing and `[Authorize(Policy = ...)]`
  per action.
- Risk of "fat controllers" by habit; code review and the architecture tests (controllers
  must not reference `Hishab.Infrastructure` types directly except through DI
  registration) guard against it.
