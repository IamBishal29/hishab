---
name: kickoff
description: One-time project kickoff for Hishab - confirm open choices, write the architecture, scaffold the .NET + React monorepo with CI, and create the GitHub backlog.
---

1. Read CLAUDE.md, PROGRESS.md and docs/PRD.md. The product scope and architecture are
   already decided; do not re-ask them.
2. Ask me with AskUserQuestion, at most 5 questions in total, only about open choices,
   e.g.: controllers vs minimal APIs, frontend package manager (pnpm vs npm), how many
   hours a day I have (to size issues), and whether to start with the Fake courier only
   or Pathao sandbox in M3.
3. Write `docs/ARCHITECTURE.md` with Mermaid diagrams: the system context (React SPA,
   API, Postgres, Hangfire, courier APIs, Gemini), the Clean Architecture layers and
   modules, the tenant isolation flow (JWT -> TenantId -> query filter), and the order
   state machine from the PRD. Write `docs/decisions/0001-stack.md` (with alternatives:
   microservices, SQL Server, MediatR) and `0002-multi-tenancy.md`, plus one ADR per
   choice from step 2.
4. Scaffold, in a `chore/kickoff-scaffold` branch:
   - `src/api`: `Hishab.sln` with Domain, Application, Infrastructure, Api projects;
     `Directory.Build.props` (net10.0, nullable, warnings as errors, analyzers),
     `Directory.Packages.props` (central package management), `.editorconfig`,
     `global.json`.
   - `tests`: `Hishab.UnitTests`, `Hishab.IntegrationTests` (Testcontainers Postgres),
     `Hishab.ArchitectureTests`, each with one passing test, plus a `/health` test.
   - `src/web`: Vite React TypeScript app, ESLint + Prettier, Vitest with one passing
     test, Tailwind + shadcn/ui set up, i18next with en and bn files.
   - `docker-compose.yml`: postgres, seq (api and web come with their issues).
   - Root: `.gitignore` (already present), `.env.example`, `README.md` (what it is,
     screenshots placeholder, architecture link, quick start).
   - `.github/workflows/ci.yml`: on pull_request and push to main; two jobs:
     api (setup-dotnet 10, restore with cache, `dotnet format --verify-no-changes`,
     build, test) and web (setup-node LTS, install with lockfile, lint, typecheck,
     test, build).
   - `.github/pull_request_template.md` (Closes #, summary, screenshots, how to test,
     review notes, checklist: tests, format/lint, learning note).
   Split scaffold and docs into two PRs if the diff gets large.
5. Draft milestones from the PRD MVP, 3-8 small issues each (goal, acceptance criteria,
   "concepts you'll learn", "interview angle"):
   M1 Foundation (tenants/shops, phone + OTP login with dev OTP in logs, JWT, roles,
      audit log, plan limits)
   M2 Orders & customers (products with cost price, quick order form, auto customer
      profile, order state machine with history, search/filters, bulk actions)
   M3 Couriers (ICourierProvider, Fake courier, Pathao sandbox, Steadfast, bulk booking,
      Hangfire status sync, PDF labels with QuestPDF)
   M4 Money & risk (COD expected vs collected, payout matching, risk badge, profit
      dashboard, CSV export)
   M5 Showcase polish (AI chat-to-order with Microsoft.Extensions.AI + Gemini, Bangla
      UI, demo seed data, Playwright e2e, README with screenshots and demo GIF)
   Show me the list; after I approve, create labels, milestones and issues (GitHub MCP,
   or `gh` / `gh api repos/{owner}/{repo}/milestones` as fallback).
6. Write `docs/learning/00-how-we-work.md`: git flow, PRs, CI, ADRs, Clean Architecture
   and monorepo layout, in plain words with a diagram.
7. Fill "Project facts" in CLAUDE.md and update PROGRESS.md.
8. Commit, push, open the PR(s), and give me the links.
