# Progress

## Current
- Milestone: M1 Foundation
- Issue: #4 done, PR #41 open (waiting for Syed's review)
- Branch: feat/4-logging-problem-details
- Next step: Syed merges PR #41, then `/next-task` picks #5 "Shop sign-up and login
  with phone number and OTP".

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
  exceptions -> ProblemDetails; 42 tests passing; learning note 02

## Decisions waiting for Syed
- `GETTING-STARTED.md` says to delete it in the first PR; the permission check blocked
  the deletion. Delete it, or say so and I'll do it in the next PR.
- PR size: #40 (~610) and #41 (~800) were over the ~300-line guideline, mostly tests.
  Plan for #5: split OTP into (a) Phone value object + OTP domain rules and (b) the
  endpoints + rate limiting, unless Syed prefers bigger PRs.
- Before #25 (labels/PDF): QuestPDF (free community licence, not open source) or an
  open-source alternative.
- Before #34 (AI chat-to-order): a Gemini free-tier API key (stays in user-secrets).
- Optional: talk to 5-10 sellers (PRD "Open questions for discovery").

## Log
- 2026-11-19: PRD and agent kit created
- 2026-11-19: Kickoff done; PRs #1 and #2 open; backlog created
- 2026-11-19: #3 implemented, PR #40 open (merged)
- 2026-11-19: #4 implemented, PR #41 open
