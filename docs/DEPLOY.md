# Deploying the free public demo (Render + Neon)

Why this setup and its limits: [ADR 0009](decisions/0009-free-demo-hosting.md). Everything here is free and
needs no credit card. About 30 minutes the first time, mostly waiting for the first Docker build.

```
browser ─► hishab-web (Render static site) ──/api/*──► hishab-api (Render, Docker) ─► Neon Postgres
```

## 1. Make the Data Protection certificate (locally)

Courier API keys are encrypted with a key ring wrapped by this certificate (ADR 0007). In Git Bash, from the repo root:

```bash
scripts/demo-secrets.sh | grep DATA_PROTECTION_CERT_BASE64 | cut -d= -f2- > ~/hishab-dp-cert.txt
```

Keep the file private (it is outside the repo). Render generates the OTP and JWT keys itself.

## 2. Create the database on Neon

1. Sign up at [neon.tech](https://neon.tech) with GitHub (Free plan).
2. New project: name `hishab-demo`, Postgres 17, region **AWS Asia Pacific (Singapore)**.
3. Dashboard → **Connect**: turn **Connection pooling off** (EF migrations and Hangfire's locks need a
   direct session), and copy the parts of the connection string.
4. Write it in the keyword format Npgsql reads (not the `postgresql://` URL):

   ```
   Host=ep-xxxx.ap-southeast-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=...;SSL Mode=Require
   ```

**This database must hold only the demo:** the API empties every app table on each start (`Demo:ResetOnStartup`).

## 3. Create both services on Render

1. Sign up at [render.com](https://render.com) with GitHub and let it see this repository.
2. **New → Blueprint**, pick the repository; Render reads `render.yaml` and lists `hishab-api` and `hishab-web`.
3. It asks for the two `sync: false` values:
   - `Database__ConnectionString`: the string from step 2.
   - `DataProtection__CertificateBase64`: the contents of `~/hishab-dp-cert.txt`.
4. **Apply**. The API's first build takes about 10 minutes (the .NET SDK and Chromium); the static site about 2.

## 4. Point the web app at the API

Open `hishab-api` in the dashboard and copy its URL. If it is not exactly `https://hishab-api.onrender.com`
(the name was taken and Render added a suffix), change the `/api/*` rewrite destination in `render.yaml`,
merge that change, then **Blueprint → Manual sync**.

## 5. Check it works

- `hishab-api` logs show `Demo reset: emptied N tables before seeding`, then `Demo shop ... seeded`.
- `https://<api>/health` answers `Healthy`.
- Open `https://<web>.onrender.com/login` and press **Sign in as Owner**: the home page shows the
  dashboard and the yellow demo banner. Print a label from a booked order (Chromium works).

## 6. Check the client-IP setting (once, and after Render changes)

The per-IP limit is 10 sign-in attempts a minute. From your laptop:

```bash
for i in $(seq 1 11); do
  curl -s -o /dev/null -w "%{http_code} " -X POST https://<web>.onrender.com/api/auth/demo \
    -H 'Content-Type: application/json' -d '{"role":"Owner"}'
done; echo
```

- Expect ten `200` then `429`. Now open the demo on your phone **on mobile data**: it must still work.
  If the phone also gets 429, every visitor shares one address: `ForwardedHeaders__ForwardLimit` is too low.
- Run the loop again with `-H 'X-Forwarded-For: 1.2.3.4'` while still limited: it must stay 429. If it
  works, a client can choose its own address: the limit is too high.

## Running the demo

- **First visit after 15 minutes idle** takes about a minute while the API wakes (and resets the data).
  Open the link yourself a minute before an interview.
- **Reset now:** `hishab-api` → **Manual Deploy → Restart service** (or deploy the latest commit).
- **Update:** auto-deploy is off. Merge to `main`, then **Manual Deploy → Deploy latest commit** on each
  service that changed.
- **Rotate secrets:** OTP/JWT keys: change them in **Environment**, then redeploy (everyone is signed out).
  The certificate: first run `TRUNCATE data_protection_keys;` in Neon's SQL editor (the old key ring cannot
  be unwrapped without the old certificate), then change the value and redeploy.
- **Do not add an uptime pinger.** It would keep Neon awake (100 compute hours a month on Free) and use
  Render's 750 free instance hours, only to hide the wake-up.
