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
- Keep each PR moderate (~600 changed lines including tests and docs, excluding lockfiles,
  migrations and generated API clients); split bigger work into more issues.

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
  `dotnet user-secrets set "Otp:HashKey" "<base64 of 32+ random bytes>" --project src/api/Hishab.Api`;
  `dotnet user-secrets set "Jwt:SigningKey" "<another base64 of 32+ random bytes>" --project src/api/Hishab.Api`;
  `DataProtection:CertificateBase64` (base64 PFX with an RSA key; README shows the openssl steps);
  `cd src/web && npm ci`. Dev OTP codes are logged (`Otp:Delivery=Log` in Development only).
- Auth: `HishabUser` (Identity, tenant-owned, one shop per user, ADR 0006). The only
  cross-tenant queries (named filter off) are the login lookup in `OtpAuthService`, the
  refresh-token lookup by hash in `AuthTokenService` (both then set the tenant from the row)
  and the "phone already in a shop" check in `StaffService.InviteAsync`.
- Tokens: JWT HS256, 15 min (`sub`, `tenant_id`, `role`); `TenantResolutionMiddleware` sets
  `ITenantContext` from `tenant_id`. Refresh tokens: one-time, SHA-256 hashed, families;
  reuse revokes the family. Browser gets an HttpOnly cookie (`hishab_rt`, Path=/api/auth),
  other clients send `tokenDelivery: "Body"`. Tests sign in for real via `factory.SignInAsync`.
- Validation: request DTOs + `AbstractValidator<T>` in `Hishab.Api` (auto-registered);
  `FluentValidationFilter` runs them globally -> 400 with camelCase field keys. Entities
  re-check invariants and throw `DomainValidationException`.
- Money: `Money` (Domain/Common, BDT, <=2 decimals, rejects rather than rounds); every Money
  property maps to `numeric(12,2)` by convention. JSON contracts expose plain `decimal`.
- Authorization: `Permission` enum + `RolePermissions` matrix (Domain/Shops); one policy per
  permission (`Policies.CanManageShop`, `CanManageStaff`, `CanEnterOrders`, `CanPack`,
  `CanSeeProfit`) checked by `PermissionHandler` from the role claim. Never authorize by role
  name on an endpoint. Matrix tests: `AuthorizationTests` (+ `_test/policy/*` probes).
  `Shop` has the tenant filter too (`Id == tenant`); cross-shop reads need
  `IgnoreQueryFilters([HishabDbContext.TenantFilter])`.
- Staff: invite = pre-created `HishabUser` (PhoneNumberConfirmed false until first OTP
  login); removal is a hard delete. Tests: `factory.InviteAndSignInAsync(client, owner, role)`
  (moves the shop to Business first); `factory.ChangePlanAsync(owner, plan)` stands in for billing.
- Plans: `Plans.LimitsFor(plan)` (PRD catalogue), `Shop.Plan` (Free default). Enforce through
  `IPlanLimitService`: `ReserveOrderAsync` (atomic upsert on `usage_counters`, call it inside
  the order's transaction) and `EnsureCanAddStaffAsync` (shop row `FOR UPDATE`, needs a
  transaction). Limit reached -> `PlanLimitExceededException` -> 402. Billing months are Dhaka
  months: always use `BillingPeriod.Containing(utcNow)`, never UTC months.
- OpenAPI: XML comments in `Hishab.Api` (`<summary>`, `<example>`) feed the document.
- Routes: every controller is under `/api` (`ApiRoutePrefixConvention`); `/health`, `/scalar`,
  `/openapi` stay at the root. JSON: string enums + strict numbers in both MVC and HTTP options.
- API contract: `src/web/openapi.json` is a snapshot checked by `OpenApiSnapshotTests`; update it
  with `HISHAB_UPDATE_OPENAPI=1 dotnet test`, then `npm run api:generate` (orval ->
  `src/web/src/api/generated`, never edit by hand; CI fails on a stale client).
- Web: access token in memory only (`features/auth/session.ts`); session = TanStack query
  `sessionQuery` (refresh cookie -> /api/auth/me); orval mutator `api/http.ts` (single-flight
  refresh, `ApiProblem`); `applyProblem` maps server field errors to RHF. Tests: MSW handlers in
  `src/test/server.ts` (unhandled requests fail). Vite proxies `/api` to :5125.
- Soft delete: implement `ISoftDeletable` (Domain/Common); the named `SoftDelete` filter hides
  deleted rows by convention; opt out with `IgnoreQueryFilters([HishabDbContext.SoftDeleteFilter])`.
  Uniqueness among active rows = partial unique index (`HasFilter("deleted_at IS NULL")`).
- Products: `Product` aggregate owns `ProductVariant`s (change variants only via Product).
  Cost prices: services check `IRequestContext.Has(Permission.SeeProfit)` (reads return null,
  writes without it -> `ForbiddenException` 403). Order lines must snapshot name/SKU/price.
- Customers: one per (TenantId, normalised E.164 phone), unique index. Use
  `ICustomerService.FindOrCreateAsync` (race-safe: unique violation -> read the winner; safe
  inside a transaction via EF savepoints). Phones never in URLs (`POST /api/customers/lookup`).
  Customer read model: `ICustomerOrderHistory` (counts by outcome, `DeliveredValueAsync` sums the
  domain `Order.Total`; computed on read, no stored counters). `GET /api/customers/{id}` returns
  `{ customer, history, deliveredValue }`; `GET /api/orders?customerId=` uses the
  `(tenant_id, customer_id, created_at, number)` index. Composite indexes: equality columns first
  (tenant included, the query filter adds it), sort key last.
- Orders: `Order` aggregate (owned `Lines` + `History`, `xmin` row version). Change status only
  via `Order.TransitionTo` (table `OrderLifecycle`, from the PRD; invalid -> 409). Lines snapshot
  name/label/SKU/price/cost. Concurrent saves of one order -> `DbUpdateConcurrencyException`
  (map to 409 "reload and retry"). Order lines have no TenantId: always query through orders.
- Order creation: `IOrderService.CreateAsync` (one transaction: customer, quota, number,
  snapshots, idempotency record). `POST /api/orders` requires `Idempotency-Key`; replays return
  the first order (201 + `Idempotent-Replayed: true`), a reused key with a different body -> 409.
  Order numbers: atomic `UPDATE shops SET last_order_number ... RETURNING`. Lock order per
  request: usage counter, then shop row (keep it, to avoid deadlocks).
- Order list: `GET /api/orders` keyset-paged on `(created_at DESC, number DESC)` with an opaque
  `OrderCursor`; indexes `(tenant, created_at, number)` and `(tenant, status, created_at, number)`.
  New list filters: check `EXPLAIN (ANALYZE, BUFFERS)` and keep the sort key last in the index.
  Dates in filters are Dhaka days: use `DhakaTime.StartOfDay` (Domain/Common), never UTC dates.
- Status changes: `IOrderService.TransitionAsync` (409 on lifecycle or concurrent change);
  bulk actions use `TransitionManyAsync` (one SaveChanges per order, per-order results with
  `BulkErrors` codes, always 200). Every staff member can read orders; changes need `CanEnterOrders`.
- Quick order form (`features/orders/QuickOrderPage`): lookup via `useCustomerLookup` (debounced,
  keyed by `normalizePhone`, 404 = new customer, returns `{ customer, history }`); auto-fill only
  empty or previously auto-filled fields; `useIdempotencyKey` keeps the key while the outcome is
  unknown (network/5xx) and drops it after success or a 4xx. `applyProblem` maps `lines[0].x` to
  `lines.0.x`. Shared phone pattern in `lib/phone.ts`. `GET /api/shop` is readable by all staff.
- Order list (`features/orders/OrderListPage`): filters live in the URL, parsed only by
  `orderFilters.ts` (untrusted input: bad values dropped); the cursor is not in the URL
  (`orderListQuery` = `useInfiniteQuery` on `nextCursor`). After any status change call
  `invalidateOrders(queryClient, ids)` (list prefix + each detail key). UI permission checks
  come from `features/auth/permissions.ts` (display only; the API decides). Order rows render via
  `features/orders/OrderRow` (optional selection; `customer` = hidden | text | link).
- Customer page (`features/customers/CustomerPage`, `/customers/:id`, Owner/Manager/Moderator):
  every customer mutation calls `afterCustomerChange(queryClient, updated)` (writes this page,
  invalidates order lists, order details and quick-order lookups). New screens that show customer
  data must be added there.
- Couriers: adapters implement `ICourierProvider` (Application/Couriers) and register as
  `ICourierProvider` singletons; `ICourierProviderRegistry` finds them by `CourierProvider` code.
  Fake courier (`Infrastructure/Couriers/Fake`): same merchant reference = same consignment ID,
  phone ending `0000` fails booking, apiKey `invalid` is rejected, statuses move via the
  `FakeCourierControl` singleton. Connections: `ICourierAccountService` (one `CourierAccount` per
  shop and courier; keys checked with the courier before the transaction; Free plan = 1 courier
  via `IPlanLimitService.EnsureCanConnectCourierAsync`; replacing keys always allowed). Read keys
  only via `GetCredentialsAsync` and never return them. Encryption: ADR 0007 (Data Protection,
  key ring in `data_protection_keys`, wrapped by the certificate; purpose includes the shop ID).
  Endpoints `/api/couriers` (GET all staff; PUT/DELETE `{provider}` need `CanManageShop`).
  Integration tests migrate before the host starts (hosted services read tables at startup).
- Booking: `IShipmentService.BookAsync` (`POST /api/orders/{id}/book`, `CanPack`). Three steps: commit
  `Shipment` = Requested (intent, reference = order ID), call `ICourierProvider.BookAsync` (must be
  idempotent on `MerchantReference`), then Shipment + Order -> Booked in one SaveChanges. Partial
  unique index `ux_shipments_one_live_per_order`; rejected shipments are retried on the same row;
  `CourierUnavailableException` -> 503 (retry is safe). `OrderDetails.shipment` = latest shipment.
  Tests: `BookingApiFactory` adds a `ScriptedCourier` (lose next answer / reject next parcel).
- Bulk booking: `IShipmentService.BookManyAsync` (`POST /api/orders/bulk/book`, 200 + per-order
  `BulkErrors`). Phases: intents one at a time (DbContext is not thread-safe), courier calls via
  `CourierBatchSender` (chunks of `ICourierProvider.MaxBulkBookingSize`, null = single calls; at
  most `Booking:MaxParallelBookings` in flight), then save each answer with `CancellationToken.None`.
  Adapters must match bulk answers by `MerchantReference`. Test factory: 2 parallel, batch 10.
- Jobs: Hangfire (schema `hangfire`, same DB; options `Jobs:*`, `RunServer=false` in tests: call jobs
  directly). Jobs take `tenantId` as an argument and call `ITenantContext.SetTenant` on a new scope
  first; they must be idempotent (at-least-once). Dashboard `/hangfire`: Development + local only.
  Status sync: recurring `CourierStatusJobs.DispatchAsync` (cross-tenant, IDs only: the 4th allowed
  cross-tenant query) enqueues `SyncShopAsync(tenant, provider)`; `ParcelStatusRules.Decide` maps
  courier status -> order moves (never skips, never guesses; Unknown/Cancelled = logged for review).
  Adapters map courier codes to `ParcelStatus` (Unknown + `CourierStatus` text when unmapped).
- Real couriers: named HttpClient via `IHttpClientFactory` + `AddStandardResilienceHandler` with
  `Retry.DisableForUnsafeHttpMethods()` (never auto-resend a booking). Base URL from options, sandbox
  by default. Logins are exchanged at connect (`CredentialCheck.ValidKeeping`), never stored; rotated
  tokens saved via `CourierCredentials.SaveRefreshedAsync` (own scope). Expired connection ->
  `CourierAuthExpiredException` (422, reconnect). Pathao (`Couriers:Pathao`): no bulk, no city/zone
  yet (#66). Contract tests: WireMock + real DI (`PathaoContractTests`); keep `TimeProvider.System`
  there (Polly waits on the DI clock).
- PDFs (ADR 0008): HTML templates printed by headless Chromium (`ChromiumPdfRenderer`, singleton, JS
  off, all requests aborted, max 2 at once); HTML-encode every value; font Noto Sans Bengali embedded
  (`Labels/Fonts`, inlined as data: URI). Labels: `GET /api/orders/labels?ids=&format=A4|Thermal`
  (`CanPack`, booked orders only). Chromium: `pwsh src/api/Hishab.Api/bin/Debug/net10.0/playwright.ps1
  install chromium` (CI adds `--with-deps`). File endpoints: `ProducesResponseType<Stream>(200,
  "application/pdf")`, no `[Produces]`; the web `apiFetch` returns non-JSON bodies as a Blob.
- Courier UI (`features/couriers`): `/settings/couriers` (Owner/Manager via `canManageShop`); key
  forms are built from `fields` in GET /api/couriers, always start empty, reset after save, and never
  put keys in the query cache (`afterCourierChange` writes only the returned connection). Booking:
  `ShipmentPanel` on order detail, bulk "Book courier" + labels on the list (`canPack`; book sends only
  Confirmed orders, labels only Booked/InTransit). Courier choice: `useBookingChoice` (shop default if
  connected, else the only one). Files: `saveFile` (lib/download.ts, download link, never
  `window.open` after an await); tests use `captureDownloads()`. Action errors: `problemMessage`.
  `applyProblem` sends server keys with no form field to `root`.
- Screenshots for UI PRs live in `docs/screenshots/NN-*.png` (390 px wide, headless Chrome).
- Field-level rules belong in services via `IRequestContext.Has(permission)`, not only in controllers.
- Audit: mark entities `IAuditable` (Domain); `AuditInterceptor` writes `AuditEntry` rows in the
  same SaveChanges. In EF config mark personal fields `.IsPersonalData(PersonalDataKind.Phone|Text)`
  and noise `.IsNotAudited()`. Do not use ExecuteUpdate/ExecuteDelete on auditable entities.
  Acting user via `IRequestContext` (`HttpRequestContext` in the API; background jobs supply one).
  Entities with a FK to shops: pass `newTenant` to `CrossTenantAssert` to create real shops.
- Run: `dotnet run --project src/api/Hishab.Api` (http://localhost:5125, `/health`,
  `/scalar`); `cd src/web && npm run dev` (http://localhost:5173).
- Test: `cd src/api && dotnet test`; `cd src/web && npm test`.
- Lint / format: `cd src/api && dotnet format --verify-no-changes`;
  `cd src/web && npm run format:check && npm run lint && npm run typecheck`.
