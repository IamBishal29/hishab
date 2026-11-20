# Progress

## Current
- Milestone: M1 Foundation
- Issue: #7 done, PR #45 open for review
- Branch: feat/7-shop-profile
- Next step: Syed merges PR #45, then `/next-task` picks #8 "Staff accounts and role-based
  authorization" (extend `Policies` into the full role matrix; staff invite by phone).

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
  ManageShop policy, OpenAPI XML examples; 142 tests passing; learning note 06

## Decisions waiting for Syed
- `GETTING-STARTED.md` says to delete it in the first PR; the permission check blocked
  the deletion. Delete it, or say so and I'll do it in the next PR.
- New: `Jwt:SigningKey` user-secret is required to run the API locally (README step 2).
- PR #45 is ~830 lines excl. migrations (guideline ~600); half tests/docs. Say if #8
  should be split up front (e.g. staff invite / role matrix).
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
- 2026-11-20: #7 implemented, PR #45 open
