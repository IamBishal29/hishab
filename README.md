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
# OTP hashing key: any random 32+ bytes, base64 (PowerShell: [Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 })))
dotnet user-secrets set "Otp:HashKey" "$(openssl rand -base64 32)" --project src/api/Hishab.Api
# JWT signing key: another, different random 32+ bytes, base64
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 32)" --project src/api/Hishab.Api
# Data Protection certificate: wraps the key ring that encrypts courier API keys (ADR 0007).
# Development only (self-signed). MSYS_NO_PATHCONV stops Git Bash on Windows rewriting "/CN=...".
MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 730 \
  -subj "/CN=Hishab dev Data Protection" -keyout dp.key -out dp.crt
openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp.pfx -passout pass:
dotnet user-secrets set "DataProtection:CertificateBase64" "$(base64 -w0 dp.pfx)" --project src/api/Hishab.Api
rm dp.key dp.crt dp.pfx   # keep a backup elsewhere in production: losing it means sellers reconnect couriers
dotnet run --project src/api/Hishab.Api           # dev OTP codes appear in the console log

# 3. Web  ->  http://localhost:5173 (proxies /api to the API, so run both)
cd src/web && npm ci && npm run dev

# API contract changed? Refresh the snapshot, then regenerate the typed client
cd src/api && HISHAB_UPDATE_OPENAPI=1 dotnet test --filter-class "*OpenApiSnapshotTests"
cd src/web && npm run api:generate
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
