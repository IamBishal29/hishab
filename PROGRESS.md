# Progress

## Current
- Milestone: M2 Orders & customers (M1 complete)
- Issue: #12 done, PR #50 open for review
- Branch: feat/12-products
- Next step: Syed merges PR #50, then `/next-task` picks #13 "Customers auto-created from
  phone, with blocklist and notes" (phones are personal data: mask in logs and audit).

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
  client regenerated; 253 .NET + 11 web tests; learning note 11

## Decisions waiting for Syed
- `GETTING-STARTED.md` says to delete it in the first PR; the permission check blocked
  the deletion. Delete it, or say so and I'll do it in the next PR.
- New: `Jwt:SigningKey` user-secret is required to run the API locally (README step 2).
- Local setup: no `.env` yet, so `docker compose up -d` fails until you `cp .env.example .env`
  and set POSTGRES_PASSWORD (I used a throwaway container for the smoke test).
- Web follow-ups: Bangla server messages (API errors are English), route code-splitting
  (bundle 181 kB gzip), 4 moderate dev-only npm audit findings.
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
  cost writes: Syed's choices)
