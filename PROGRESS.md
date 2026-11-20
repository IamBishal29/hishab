# Progress

## Current
- Milestone: M2 Orders & customers (M1 complete)
- Open PRs: #58 (#18 order list, landing #57 on main; CI green) and #60 (#56 customer history API)
- Branch: feat/56-customer-history-api
- Next step: Syed merges #58, then #60 (PROGRESS.md conflicts: keep #60's version, it includes
  #18). Then `/next-task` picks #59 "Web: customer page" (needs both on main).

## Done
- Product discovery: PRD written (docs/PRD.md); name chosen: Hishab
- Stack agreed: .NET 10 modular monolith + React, PostgreSQL, free tools only
- Agent kit added (CLAUDE.md, skills, settings)
- Kickoff (2026-11-19):
  - Choices: controllers, npm, 4+ hours a day, Fake courier first then Pathao sandbox
  - PR #1: ARCHITECTURE.md, ADRs 0001-0005, learning note 00
  - PR #2: .NET 10 solution (4 layers), unit/architecture/integration tests (5 passing),
    React app (Vite, Tailwind, shadcn/ui, i18next en+bn, Vitest), docker compose
    (postgres, seq), CI (api + web jobs, green), PR template, README
  - Backlog: 5 milestones, 37 issues (#3-#39), area and module labels
  - PRs #1 and #2 merged
- #3 Persistence + tenant isolation (PR #40): HishabDbContext, named tenant filter by
  convention, SaveChanges tenant rules, baseline migration, DbContext health check,
  CrossTenantAssert helper, model guard tests; 17 tests passing; learning note 01
  (merged)
- #4 Logging + errors (PR #41): Serilog (options, console/Seq), correlation ID
  middleware, TenantId/UserId log context, PhoneMasker + redaction enricher, domain
  exceptions -> ProblemDetails; 42 tests passing; learning note 02 (merged)
- #5 PR A (#42): ADR 0006 one shop per user, Phone value object, OtpChallenge rules,
  OTP contracts; 72 tests passing; learning note 03 (merged)
- #5 PR B (#43): Shop, Identity HishabUser (tenant-owned), migration, HMAC hasher,
  OtpAuthService (per-phone limits, uniform failures, one-transaction sign-up),
  /auth/otp + /auth/otp/verify, IP rate limiter; 91 tests passing; learning note 04
  (merged)
- #6 JWT + refresh rotation (PR #44): RefreshToken entity (families, hashed, one-time,
  xmin), HS256 access tokens (15 min), TenantResolutionMiddleware, /auth/refresh,
  /auth/logout, /auth/me, HttpOnly cookie by default or body on request; TestAuthHandler
  replaced by real sign-in; 111 tests passing; learning note 05 (merged)
- #7 Shop profile (PR #45): Money value object, Shop profile + delivery charges (60/120
  default, backfilled), shop tenant filter, GET/PUT /shop, FluentValidation global filter,
  ManageShop policy, OpenAPI XML examples; 142 tests passing; learning note 06 (merged)
- #8 Staff + authorization (PR #46): Permission enum + RolePermissions matrix, one policy
  per permission (PermissionHandler), POST/GET/PUT role/DELETE /staff (Owner only), invite =
  pre-created user joining at first OTP login, hard delete, limit placeholder 15; 183 tests
  passing; learning note 07 (merged)
- #9 Audit log (PR #47): IAuditable + AuditInterceptor (same transaction, old/new diff,
  personal fields masked, secrets never stored, append-only), AuditEntry (jsonb, sequence
  order, no FK to users), IRequestContext, GET /audit (Owner, paged); 198 tests passing;
  learning note 08 (merged)
- #10 Web shell + login (PR #48): API under /api, OpenAPI snapshot test + orval client
  (CI drift check), contract fixes (string enums, strict numbers, required fields), fetch
  core (memory token, single-flight refresh), session query, router shell, login/OTP/setup
  (RHF + Zod, server field errors, en + bn); 199 .NET + 11 web tests; e2e smoke through the
  Vite proxy; learning note 09 (merged; M1 complete)
- #11 Plan limits (PR #49): Plan catalogue from the PRD,
  Shop.Plan (Free default, backfilled), BillingPeriod (Dhaka months), usage_counters with an
  atomic reserve (no overshoot under parallel load), staff limit with a shop row lock (fixes
  the #8 race), 402 PlanLimitExceeded, GET /api/shop/usage, client regenerated; 221 .NET +
  11 web tests; learning note 10 (merged)
- #12 Products (PR #50): Product aggregate + variants (SKU rules, soft delete), soft-delete
  filter convention, partial unique SKU index, ManageProducts (Owner, Manager), owner-only
  cost prices enforced in the service (IRequestContext.Has, 403 on writes), search/paging,
  client regenerated; 253 .NET + 11 web tests; learning note 11 (merged)
- #13 Customers (PR #51): Customer per (tenant, E.164 phone) with unique index, race-safe
  find-or-create (unique violation -> read winner; savepoint-safe), POST lookup (phone in body),
  block/unblock, notes, search by name/digits, personal data masked in audit, IDs-only logs;
  270 .NET + 11 web tests; learning note 12 (merged)
- #14 Order aggregate (PR #52): Order + owned lines (snapshots incl. cost) + status history
  (sequence), OrderLifecycle table from the PRD (TransitionTo, 409 on invalid), totals/COD,
  xmin row version, concurrent saves -> DbUpdateConcurrencyException; 9 allowed + 72
  forbidden transition tests; 365 .NET tests; learning note 13 (merged)
- #15 Quick order API (PR #53): POST /api/orders in one transaction (customer, quota, order
  number, catalogue snapshots, delivery charge rule/override), required Idempotency-Key
  (fingerprint, replay 201 + header, 409 on reuse, parallel twins roll back and replay),
  customer history summary + blocked warning, GET /api/orders/{id}; 378 .NET tests; note 14
  (merged)
- #16 Order search + bulk (PR #54): GET /api/orders (status, Dhaka dates, area, source, text;
  keyset cursor on (created_at, number)), indexes checked with EXPLAIN (keyset 7 buffers vs
  OFFSET 847 + disk sort at row 40k), confirm/cancel (409 on lifecycle or concurrent change),
  bulk confirm/cancel with per-order results, DhakaTime helper; 395 .NET tests; note 15 (merged)
- #17 Quick order form (PR #55): phone-first debounced lookup (auto-fills only empty fields,
  history + blocked warning), product picker, live totals, RHF + Zod with server line errors,
  idempotency key kept while the outcome is unknown; lookup returns { customer, history };
  GET /api/shop readable by all staff; screenshots in docs/screenshots; 397 .NET + 30 web
  tests; note 16
- #18 Order list + detail (PR #57 merged into the #17 branch after #55 had landed, so it never
  reached main; re-opened as PR #58 against main, CI green): URL filters (orderFilters.ts,
  bad values dropped), keyset "Load more" (useInfiniteQuery), bulk confirm/cancel with per-row
  results, order detail timeline + confirm/cancel (409 message), invalidateOrders after changes,
  packer read-only view; customer page split to #56; 45 web tests; note 17
- #56 Customer history API (PR #60): GET /api/customers/{id} -> { customer, history,
  deliveredValue } (computed on read from Order.Total), GET /api/orders?customerId= with a
  (tenant, customer, created_at, number) index (EXPLAIN: 6 vs 84 buffers for a 2,000-order buyer
  once tenant leads the index); 400 .NET tests; note 18. Web half split to #59.

## Decisions waiting for Syed
- `GETTING-STARTED.md` says to delete it in the first PR; the permission check blocked
  the deletion. Delete it, or say so and I'll do it in the next PR.
- New: `Jwt:SigningKey` user-secret is required to run the API locally (README step 2).
- Local setup: no `.env` yet, so `docker compose up -d` fails until you `cp .env.example .env`
  and set POSTGRES_PASSWORD (I used a throwaway container for the smoke test).
- Web follow-ups: Bangla server messages (API errors are English), route code-splitting
  (bundle 181 kB gzip), 4 moderate dev-only npm audit findings.
- Order lifecycle follows the PRD exactly: no cancel after booking, no return after delivery.
  Confirm with sellers whether courier-side cancellations need a Booked -> Cancelled move.
- Quick order: no per-order price override (catalogue price only) and Idempotency-Key is
  required; say if sellers need a negotiated-price override. Old idempotency keys need a
  clean-up job once Hangfire lands.
- Order list (#16): courier filter waits for courier booking; "area" = inside/outside Dhaka for
  now; bulk returns 200 with per-order results (207 is the alternative); ILIKE search would
  need a pg_trgm index at very large volumes.
- Quick order form (#17): GET /api/shop opened to all staff (delivery charges for the form);
  form keeps the order channel after save; PR is ~1,200 lines (could have been split
  backend/web). Smoke-tested against a throwaway Postgres container with process-only secrets.
- Order list (#18): PR is ~1,250 lines (pages ~550, tests/fakes ~370, strings ~130); say if you
  want order detail split out. Filters take a lot of height at 360 px (could collapse later).
  Integration tests not run locally (Docker was off); web-only change, CI runs them.
- Stacked PRs: a stacked PR merged into its base branch after that base has landed does not
  reach main. From now on I branch from main only, or retarget before you merge.
- Git's credential manager has no GitHub login on this machine (push failed with "could not read
  Username"); I pushed via `gh auth git-credential` for that one command. To fix for good:
  `gh auth setup-git` (or sign in again through Git Credential Manager).
- Later hardening for the audit log: DB role without UPDATE/DELETE on audit_entries, or a
  hash chain (tamper-evidence); decide when deployment is planned.
- Later: optimistic concurrency (ETag + xmin) for PUT /shop if two managers editing at
  once becomes real; validate default courier against connected couriers (courier issue).
- Web login (#10): the SPA must serialise refresh calls (strict reuse detection: two tabs
  refreshing at once means one gets 401) and use a Vite proxy so the `/auth` cookie is
  same-origin.
- `.env.example` is blocked by permission settings (`.env*` rule), so I cannot keep its
  placeholders up to date; allow `.env.example` explicitly if you want me to.
- Before #25 (labels/PDF): QuestPDF (free community licence, not open source) or an
  open-source alternative.
- Before #34 (AI chat-to-order): a Gemini free-tier API key (stays in user-secrets).
- Optional: talk to 5-10 sellers (PRD "Open questions for discovery").

## Log
- 2026-11-19: PRD and agent kit created
- 2026-11-19: Kickoff done; PRs #1 and #2 open; backlog created
- 2026-11-19: #3 implemented, PR #40 open (merged)
- 2026-11-19: #4 implemented, PR #41 open (merged)
- 2026-11-19: #5 PR A opened (#42, merged); user<->shop decided (ADR 0006)
- 2026-11-19: #5 PR B opened (#43, merged)
- 2026-11-20: PR size guideline raised to ~600 lines incl. tests/docs (Syed)
- 2026-11-20: #6 implemented, PR #44 open (refresh token: cookie + body, Syed's choice) (merged)
- 2026-11-20: #7 implemented, PR #45 open (merged)
- 2026-11-20: #8 implemented, PR #46 open (merged)
- 2026-11-20: No AI-tool attribution on GitHub from now on (Syed)
- 2026-11-20: #9 implemented, PR #47 open (merged)
- 2026-11-20: #10 implemented, PR #48 open (API under /api and one PR: Syed's choices) (merged)
- 2026-11-20: #11 implemented, PR #49 open (usage counter table: Syed's choice) (merged)
- 2026-11-20: #12 implemented, PR #50 open (all read, Owner+Manager edit; 403 on manager
  cost writes: Syed's choices) (merged)
- 2026-11-20: #13 implemented, PR #51 open (phone lookup via POST body: Syed's choice) (merged)
- 2026-11-20: #14 implemented, PR #52 open (merged)
- 2026-11-20: #15 implemented, PR #53 open (merged)
- 2026-11-20: #16 implemented, PR #54 open (keyset cursor paging: Syed's choice) (merged)
- 2026-11-20: #17 implemented, PR #55 open
- 2026-11-20: #18 split (customer page -> #56) and stacked on #55 (Syed's choices); PR #57 open
- 2026-11-20: #57 merged into the #17 branch, not main; re-opened as #58. #56 split (web -> #59),
  API half from main (Syed's choice); PR #60 open
