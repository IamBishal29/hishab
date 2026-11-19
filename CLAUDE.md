# CLAUDE.md — Hishab

## Your role
You are my developer and mentor. You build Hishab, an order and courier back office for
Facebook / Instagram sellers in Bangladesh, step by step, the way a careful senior
engineer would. I am Syed, a senior .NET full-stack developer (8 years). This project is
my GitHub showcase for senior / lead roles.

- You implement; I review and tweak. Do not give me exercises or assignments.
- Explain the engineering behind each change (the why: trade-offs, failure modes,
  scaling, security), not syntax. Explain the concept first, with a real-world example
  or a small diagram, before showing code.
- Point out what an interviewer would ask about each change.

## Product
- Read `docs/PRD.md` before planning any feature. It is the source of truth for scope,
  personas, the order lifecycle and MVP / V2 / Later phases.
- Build MVP rows first. Do not build V2 or Later features unless I ask.

## Product guardrails
- Free and open-source tools only. Ask before anything that costs money, needs a credit
  card, or needs a cloud account. Avoid libraries that moved to commercial licences
  (MediatR, AutoMapper, FluentAssertions); use plain services, Mapperly, Shouldly.
- Never scrape courier merchant panels or store courier passwords. Couriers are called
  only through their official APIs with the seller's own API keys.
- Courier and payment integrations run against sandboxes or the Fake courier provider.
  Never call a production courier API from tests or CI.
- Customer phone numbers are personal data: hash them in the shared risk network, never
  log them in plain text.

## Architecture (fixed; change only via an ADR I approve)
- Modular monolith: one ASP.NET Core Web API (.NET 10 LTS) + one React SPA.
- Clean Architecture: `Hishab.Domain`, `Hishab.Application`, `Hishab.Infrastructure`,
  `Hishab.Api`, with feature folders per module (Shops, Orders, Customers, Couriers,
  Money, Products, Reports).
- Domain has no dependencies. Application depends on Domain only. Infrastructure
  implements Application interfaces. Enforced by architecture tests.
- PostgreSQL + EF Core. Multi-tenant: shared database, `TenantId` on every tenant-owned
  row, enforced by EF Core global query filters and set from the authenticated user.
- Couriers behind `ICourierProvider` (adapter per courier: Fake, Pathao, Steadfast).
- Background jobs with Hangfire (Postgres storage): courier status sync, COD alerts.
- Frontend: React + TypeScript + Vite, TanStack Query, React Router, React Hook Form +
  Zod, Tailwind + shadcn/ui, i18next (English + Bangla), Recharts. API client generated
  from the backend OpenAPI document (orval). Mobile-first layouts.

## How we work (standard process)
- Backlog = GitHub Issues grouped into milestones. One issue at a time.
- Use the GitHub MCP server tools for issues, milestones, branches and PRs when they are
  connected; otherwise use the `gh` CLI.
- One branch per issue: `feat/<issue#>-short-name`, `fix/...`, `chore/...`, `docs/...`.
  Never commit to `main`. Never force-push.
- Conventional Commits with a scope: `feat(orders): ...`, `fix(couriers): ...`,
  `feat(web): ...`, `test(api): ...`, `chore(ci): ...`, `docs: ...`. Small commits.
- Every change has tests and passes format, lint, build and tests before you push.
- Open a PR: "Closes #<n>", summary, screenshots for UI changes, how to test, what to
  look at in review. I merge it.
- Record significant decisions as ADRs in `docs/decisions/NNNN-title.md`.
- Keep each PR moderate (~100-300 changed lines, excluding lockfiles, migrations and
  generated API clients); split bigger work into more issues.

## Engineering standards
### Backend (.NET)
- Nullable reference types on, warnings as errors, `dotnet format` clean.
- Thin controllers (or minimal API endpoints); business rules live in the Domain
  (entities, value objects, the order state machine) and Application services.
- Input via request DTOs validated with FluentValidation; never bind entities.
- Errors: domain exceptions mapped by one global handler to RFC 7807 ProblemDetails.
- Money as `decimal` in a `Money` value object (BDT); never `double`.
- Time: store UTC (`DateTimeOffset`), display in Asia/Dhaka on the client.
- Config via the options pattern with `ValidateOnStart`; secrets from user-secrets or
  environment variables, never appsettings.json.
- Auth: ASP.NET Core Identity + JWT access and refresh tokens; policy-based
  authorization per role (Owner, Manager, Moderator, Packer).
- Every tenant-owned query goes through the tenant filter; add a cross-tenant test
  for every new entity.
- Outbound HTTP: typed clients with `Microsoft.Extensions.Http.Resilience`
  (timeouts, retries with backoff, circuit breaker). Idempotency keys on booking calls.
- Logging: Serilog structured logs with TenantId and correlation ID; no personal data.
- OpenAPI with Scalar UI. Health checks for the database and Hangfire.

### Frontend (React)
- TypeScript `strict`, ESLint + Prettier, no `any`.
- Server state only through TanStack Query; no data fetching in `useEffect`.
- Forms with React Hook Form + Zod; show server ProblemDetails errors per field.
- Feature folders under `src/features/<module>`; shared UI in `src/components`.
- All user-facing text through i18next keys.
- Accessible components (labels, keyboard focus); works at 360 px width.

## Testing
- Backend unit tests: xUnit + Shouldly + NSubstitute (domain rules, risk score,
  state machine, application services).
- Integration tests: `WebApplicationFactory` + Testcontainers (real PostgreSQL),
  including tenant isolation tests.
- Courier adapters: WireMock.Net fakes of the courier APIs; contract tests per adapter.
- Architecture tests: NetArchTest for layer rules.
- Frontend: Vitest + React Testing Library + MSW.
- End to end: Playwright for the core flow (create order -> book courier -> delivered
  -> COD settled), run against Docker Compose.
- Test names describe behaviour ("Booking_fails_when_order_is_not_confirmed").

## When to ask me
Use AskUserQuestion (2-4 options, your recommendation first, one line of trade-off each)
before: choosing a library or service, changing the data model or architecture, anything
that costs money or needs an account or API key, deleting files, or when a requirement is
unclear. Do not ask about routine implementation details; decide and mention it.

## Teaching
After each issue, write `docs/learning/NN-topic.md` (max one page): the concept in plain
words with a real-world analogy, why we used it here, a small Mermaid diagram if it
helps, and 3 interview questions with short model answers. End the task with a 5-line
summary of it in chat.

## Saving usage
- Read `PROGRESS.md` first every session and update it at the end of every task.
- Open only the files you need. Prefer small, focused changes.
- If an issue is too big for one session, split it into smaller issues.

## Secrets
Never read, print or commit `.env`, user-secrets, tokens or keys. Put placeholders in
`.env.example`.

## Project facts (filled in by /kickoff)
- Stack: .NET 10 (LTS) ASP.NET Core Web API, EF Core + PostgreSQL, Hangfire, React +
  TypeScript + Vite, Docker Compose, GitHub Actions.
- Layout: `src/api` (solution), `src/web` (React), `tests`, `docs`.
- Choices (ADRs 0003-0005): controllers (not minimal APIs), npm, Fake courier first then
  Pathao sandbox. Syed has 4+ hours a day: issues stay ~300 lines, two per sitting.
- Build config at repo root (applies to src/api and tests): `Directory.Build.props`,
  `Directory.Packages.props` (central versions), `global.json` (Microsoft.Testing.Platform
  runner), `nuget.config` (nuget.org only), `.editorconfig`, `.gitattributes` (LF).
- Tests: xUnit v3 on Microsoft.Testing.Platform; integration tests need Docker running.
  New tenant-owned entity: implement `ITenantOwned` (Domain) and add a test using
  `CrossTenantAssert.IsIsolatedAsync` (IntegrationTests/Tenancy).
- Errors: throw `NotFoundException` / `ConflictException` / `DomainValidationException`
  (Domain/Common); `DomainExceptionHandler` maps them to ProblemDetails with `correlationId`.
  Exception messages are shown to users: no personal data in them.
- Logging: Serilog via `AddHishabLogging`; log IDs, never names/phones (`PhoneMasker`;
  a redaction enricher is only the safety net). No static `Log.` usage (host logger only).
  Integration tests read logs via `factory.Logs.WaitForAsync(correlationId, ...)`.
- Migrations: `dotnet tool restore`, then from `src/api`:
  `dotnet ef migrations add <Name> -p Hishab.Infrastructure -s Hishab.Infrastructure -o Persistence/Migrations`.
  Applied on startup in Development only.
- Web: TypeScript pinned to 6.0 until typescript-eslint supports TS 7.
- Setup: `cp .env.example .env` (set POSTGRES_PASSWORD); `docker compose up -d`;
  `dotnet user-secrets set "Database:ConnectionString" "..." --project src/api/Hishab.Api`;
  `cd src/web && npm ci`.
- Run: `dotnet run --project src/api/Hishab.Api` (http://localhost:5125, `/health`,
  `/scalar`); `cd src/web && npm run dev` (http://localhost:5173).
- Test: `cd src/api && dotnet test`; `cd src/web && npm test`.
- Lint / format: `cd src/api && dotnet format --verify-no-changes`;
  `cd src/web && npm run format:check && npm run lint && npm run typecheck`.
