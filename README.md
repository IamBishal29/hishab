# Hishab

**The back office for Facebook and Instagram sellers in Bangladesh:** take an order in
20 seconds, book the courier in one click, and stop losing money on fake orders and
unpaid cash on delivery (COD).

> Status: early development. See the [PRD](docs/PRD.md) and the
> [milestones](../../milestones).

## Screenshots

_Coming with the first UI milestone._

## Architecture

A modular monolith: ASP.NET Core Web API (.NET 10) with Clean Architecture, PostgreSQL,
Hangfire and a React + TypeScript SPA. Multi-tenant (shared database, `TenantId` +
EF Core global query filters). Couriers sit behind one `ICourierProvider` interface.

- [Architecture overview with diagrams](docs/ARCHITECTURE.md)
- [Architecture decision records](docs/decisions/)

```text
src/api    .NET solution (Domain, Application, Infrastructure, Api)
src/web    React SPA (Vite, Tailwind, shadcn/ui, i18next en + bn)
tests      Unit, integration (Testcontainers) and architecture tests
docs       PRD, architecture, ADRs, learning notes
```

## Quick start

Requirements: .NET 10 SDK, Node.js LTS, Docker.

```bash
# 1. Infrastructure (PostgreSQL + Seq)
cp .env.example .env              # then set POSTGRES_PASSWORD
docker compose up -d

# 2. API  ->  http://localhost:5125/health, docs at /scalar
dotnet user-secrets set "Database:ConnectionString" \
  "Host=localhost;Port=5432;Database=hishab;Username=hishab;Password=<your password>" \
  --project src/api/Hishab.Api
dotnet run --project src/api/Hishab.Api

# 3. Web  ->  http://localhost:5173
cd src/web && npm ci && npm run dev
```

## Tests and checks

```bash
# API (needs Docker running for the integration tests)
cd src/api && dotnet format --verify-no-changes && dotnet build && dotnet test

# Web
cd src/web && npm run format:check && npm run lint && npm run typecheck && npm test && npm run build
```

## Licence

See [LICENSE](LICENSE).
