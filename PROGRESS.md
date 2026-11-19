# Progress

## Current
- Milestone: M1 Foundation
- Issue: none in progress
- Branch: main
- Next step: Syed merges PR #1 (docs) and PR #2 (scaffold), then runs `/next-task`,
  which picks #3 "Persistence: EF Core, PostgreSQL and the tenant query filter".

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

## Decisions waiting for Syed
- `GETTING-STARTED.md` says to delete it in the first PR; the permission check blocked
  the deletion. Delete it, or say so and I'll do it in the next PR.
- Before #25 (labels/PDF): QuestPDF (free community licence, not open source) or an
  open-source alternative.
- Before #34 (AI chat-to-order): a Gemini free-tier API key (stays in user-secrets).
- Optional: talk to 5-10 sellers (PRD "Open questions for discovery").

## Log
- 2026-11-19: PRD and agent kit created
- 2026-11-19: Kickoff done; PRs #1 and #2 open; backlog created
