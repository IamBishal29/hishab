# Developing Hishab

How to run the API and the web app from source, the Docker setup, the demo shop and the checks CI runs.
Back to the [README](../README.md).

## Run from source

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
# PDF labels: headless Chromium (ADR 0008), once per machine after the first build (about 150 MB)
dotnet build src/api && pwsh src/api/Hishab.Api/bin/Debug/net10.0/playwright.ps1 install chromium
# Optional: draft orders from a pasted chat with Gemini (free-tier key from aistudio.google.com).
# Without it the app runs normally and that one endpoint answers 503. Free-tier prompts may be
# used by Google to improve its models: use a paid key before real customer chats go through it.
dotnet user-secrets set "Ai:Provider" "Gemini" --project src/api/Hishab.Api
dotnet user-secrets set "Ai:ApiKey" "<your Gemini API key>" --project src/api/Hishab.Api
dotnet run --project src/api/Hishab.Api           # dev OTP codes appear in the console log

# 3. Web  ->  http://localhost:5173 (proxies /api to the API, so run both)
cd src/web && npm ci && npm run dev

# API contract changed? Refresh the snapshot, then regenerate the typed client
cd src/api && HISHAB_UPDATE_OPENAPI=1 dotnet test --filter-class "*OpenApiSnapshotTests"
cd src/web && npm run api:generate
```

## Run the whole product with Docker

One command builds the images, migrates, seeds the demo shop and starts everything behind health checks:

```bash
cp .env.example .env                 # set POSTGRES_PASSWORD
scripts/demo-secrets.sh >> .env      # OTP, JWT and Data Protection secrets, generated locally
docker compose --profile app up -d --build --wait
# -> http://localhost:8080 (WEB_PORT=8090 if 8080 is taken); sign in as 01000000001
docker compose logs api | grep OTP   # the sign-in code (no SMS gateway yet)
```

End-to-end test against it (Playwright, the core flow: order -> book -> delivered -> COD settled):

```bash
cd e2e && npm ci && npx playwright install chromium && npm test
```

## Demo shop

In Development the API seeds a fictional shop at startup (`Demo:Seed`, on in `appsettings.Development.json`):
"Nodi Boutique (Demo)" with staff, six products, twelve customers, 30 orders in every status over the last four
weeks, Fake courier bookings and three weekly COD payouts. Sign in as the owner with **01000000001** (the OTP
code is in the API log); staff are 01000000002 (Manager), …03 (Moderator), …04 (Packer).

- Every phone number is in the fictional block `+880 10 0000 0xxx`: 010 is not issued to any operator.
- Re-running is safe: each step checks whether it already happened, so nothing is duplicated.
- The API refuses to start if `Demo:Seed` is on in any environment other than Development or `Demo`.

## Tests and checks

```bash
# API (needs Docker running for the integration tests, and Chromium installed for the PDF label tests)
cd src/api && dotnet format --verify-no-changes && dotnet build && dotnet test

# Web
cd src/web && npm run format:check && npm run lint && npm run typecheck && npm test && npm run build
```

## Refresh the README screenshots and GIF

Use a separate compose project, so fresh demo data never touches your development database:

```bash
docker compose -p hishab-media --profile app up -d --build --wait
cd e2e
export E2E_API_LOGS="docker compose -p hishab-media --profile app logs api --no-color --no-log-prefix"
npm run readme:media               # writes docs/screenshots/readme/*.png and demo.gif
cd ..
docker compose -p hishab-media --profile app down -v   # removes only the hishab-media volumes
```
