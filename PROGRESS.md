# Progress

## Current
- Milestone: M5 Showcase polish
- Issue: #36 in review (PR #86); #35 done (PR #85 merged)
- Branch: feat/36-bangla-ui (pushed)
- Next step: after #86 merges, #37 "Demo seed data" (lowest open M5 issue), then #38 (compose + e2e), #39 (README).

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
- #59 Customer page (PR #61): track record tiles (incl. delivery success and delivered value),
  details form (server field errors), block/unblock, the customer's orders; afterCustomerChange()
  fans one change out to order lists, order details and quick-order lookups (test guards it);
  OrderRow extracted and shared; customer links for Owner/Manager/Moderator; 52 web tests; note 19
- #19 Couriers (PR #62): ICourierProvider + registry, deterministic Fake courier (scriptable via
  FakeCourierControl), CourierAccount with keys encrypted by Data Protection (key ring in Postgres,
  wrapped by a certificate, purpose bound to the shop; ADR 0007), masked hints only, Free plan = 1
  courier (shop-row lock; key rotation always allowed), GET/PUT/DELETE /api/couriers; tests migrate
  before host start; 421 .NET tests; note 20. README commands smoke-tested end to end.
- #20 Booking (PR #63): Shipment entity (Requested/Booked/Rejected, row version), intent committed
  before the courier call, reference = order ID (adapters must be idempotent on it), partial unique
  index = one live booking per order, POST /api/orders/{id}/book (CanPack), 503 on no answer,
  OrderDetails.shipment, default courier cleared on disconnect; 440 .NET tests; note 21
- #21 Bulk booking (PR #64): POST /api/orders/bulk/book (200 + per-order results), intents one at a
  time, courier calls via CourierBatchSender (bulk chunks or single calls, max 4 in flight, config),
  answers saved even if the caller leaves, failures retry-safe; 449 .NET tests in total; note 22
- #22 Status sync (PR #65): Hangfire on Postgres (schema `hangfire`), recurring dispatcher -> one job per
  (shop, courier) with the tenant set explicitly, ParcelStatusRules (lifecycle-driven, idempotent, never
  skips; Cancelled/Unknown flagged), Shipment status columns + partial tracking index, hangfire health
  check, dashboard Development + local only; 476 .NET tests; note 23
- #23 Pathao (PR #67): adapter with login exchanged once (tokens stored, never the password), cached
  tokens with single-flight refresh + rotated tokens saved, resilience pipeline (no auto-retry of POST,
  circuit breaker), 422/duplicate/401/5xx handling, status mapping; 14 WireMock contract tests; 490 .NET
  tests; note 24. City/zone lookup split to #66. (merged)
- #25 Labels (PR #69): GET /api/orders/labels (A4 8 per page, thermal 100x150 mm), HTML printed by
  headless Chromium (ADR 0008: Bangla shaping, Noto Sans Bengali embedded, JS off, all requests aborted,
  max 2 renders), 3 queries per batch, 400 names unbooked orders, 503 when Chromium is missing; web
  apiFetch returns Blobs; CI installs Chromium; 501 .NET + 53 web tests; note 25. Invoices split to #68.
- #26 Courier UI (PR #70): /settings/couriers (write-only keys from server-driven fields, replace,
  disconnect, default), ShipmentPanel on order detail (book, refusal shown once, label download),
  bulk book + labels on the list (packers can select; only Confirmed sent), PDFs downloaded not
  opened, applyProblem routes unmatched server keys to root; 70 web tests; note 26; screenshots
- #66 Courier locations, backend (PR #72): SupportsLocations + GetLocationsAsync (Pathao city/zone/area
  lists, Fake tree), ICourierLocationService (IMemoryCache 24 h, empty lists not cached, zones only for
  listed cities, EnsureValidAsync), CourierDestination on Shipment (nullable columns + check constraint,
  frozen while Requested), destination on /book, POST /orders/{id}/quote (Pathao price-plan), GET
  /couriers/{p}/locations, Pathao JSON omits nulls; 536 .NET tests; note 27. Web picker split to #71.
- #68 Invoices (PR #73): GET /api/orders/invoices (A5 default / A4, one per page, CanPack, not for
  Cancelled), Invoice/InvoiceLine allow-list without costs (reflection guard), PrintHtml shared with
  labels, Dhaka date; 548 .NET tests; note 28; sample screenshot
- #71 Location picker (PR #74): city -> zone -> area selects per courier with supportsLocations (one
  query per parent, 1 h stale, filter box on long lists), courier price via useQuote (query, not
  mutation), destination sent on booking and shown by name; city without zone blocks Book; 73 web
  tests; note 29; screenshots taken against Vite with a mocked API (Docker was off)
- #27 COD summary (PR #75): GET /api/money/cod-summary (CanSeeProfit): expected (Order.CodAmount of
  delivered parcels), collected (CodSettled until #28), outstanding aged in Dhaka days (0-3/4-7/8-14/15+,
  oldest unpaid), per courier + totals; pure CodReconciliation in Domain/Finance; 568 .NET tests; note 30
- #28 Payouts (PR #77): Payout aggregate with owned lines, pure PayoutMatching rules (repeat, unknown,
  already paid, not delivered, amount differs, settled), one SaveChanges for payout + CodSettled moves,
  short payments settle with a visible shortfall, mismatches saved with a reason, one statement per
  (courier, reference) and one settlement per parcel (unique indexes, race test), COD summary collected =
  paid amounts + shortfall; POST/GET /api/money/payouts; 589 .NET + 73 web tests; note 31. Returns -> #76.
- #29 Risk score (PR #78): pure CustomerRisk.Assess with levels New/Safe/Watch/Risky and every reason
  listed (blocked, high/elevated return rate, recent return forgiven by a later delivery, many
  cancellations), thresholds from Risk:* options (ValidateOnStart), history gains last delivered/returned
  dates, risk on lookup, customer page and order details (current order excluded); this shop only;
  608 .NET + 73 web tests; note 32
- #76 Returns + re-match (PR #79): receive-back (CanPack; charge needs SeeProfit, in the service),
  PUT return-charge (CanSeeProfit) for later entry/correction, `Shipment.ReturnCharge` (migration),
  `ShipmentDto.returnCharge` hidden without SeeProfit, payout re-match of UnknownParcel/NotDelivered lines
  only (amounts unchanged, settled-shipment index guards races); 620 .NET + 73 web tests; note 33
- #30 Dashboard API (PR #80): GET /api/reports/today and /profit (CanSeeProfit), pure ProfitReport.Build
  (sales on the delivery day, returns on the return day, fees from payouts else booking quote, unknowns 0 +
  `missing` counts), tenant_id shadow column on order_status_changes (backfilled) + index (tenant_id, to,
  changed_at); 635 .NET + 73 web tests; note 34
- #31 CSV export (PR #81): GET /api/export/orders.csv + customers.csv (Owner only, no-store), streamed with
  AsAsyncEnumerable, CsvWriter (RFC 4180, UTF-8 BOM, CSV-injection neutralising for typed text, phones as
  01912-345678), list filters shared with the screens, EnumSchemaTransformer fix for nullable shared enums;
  655 .NET + 73 web tests; note 35
- #32 Money screens (PR #82): /money (COD totals, per-courier ageing, payouts with "needs a look"),
  payout form with statement paste (pure parseStatement, unreadable rows listed), payout detail with
  mismatches first + re-match, Owner-only (queries disabled for others), afterPayoutChange fan-out;
  86 web tests; note 36; screenshots
- #32 merge note: GitHub made the merge commit (8e46349) but never flagged PR #82 as merged; I fast-forwarded
  the branch to main (PR shows closed) and closed #32 by hand with a comment.
- #33 Dashboard + risk badges (PR #83): `customerRisk` on order list rows via ForManyAsync (two grouped queries
  per page, shared Build with the customer page), RiskBadge (word + icon shape + colour + reasons) on lookup,
  list, detail and customer page, Owner dashboard (today cards, lazy Recharts profit chart 30 days / 12 months,
  caption + table, missing-cost note); 656 .NET + 107 web tests; note 37; screenshots
- #34 Chat-to-order API (PR #84): POST /api/orders/drafts/from-chat (CanEnterOrders, saves nothing), Gemini via
  Google.GenAI behind IChatClient (Ai:Provider None by default -> 503), phones masked to [PHONE_n] before the model,
  pure OrderDraftRules (real token or no phone, catalogue refs only, clamps, trims, warning codes), fenced chat,
  JSON schema at temperature 0, timeout, per-shop rate limit (6/min), counts-only logs; 682 .NET + 107 web tests;
  note 38
- #35 Paste chat UI (PR #85): ChatDraftPanel on the quick order form, word + symbol "Suggested" marks derived from
  value === suggestion (editing removes them), fills only untouched or still-suggested fields, matched lines added
  (draft lines now carry label + unitPrice), unmatched items + warnings in a live region, 503/429 messages, en + bn;
  114 web tests; note 39; screenshot (merged)
- #36 Bangla UI (PR #86): Bangla default + saved choice (`hishab.language`, `<html lang>`), Noto Sans Bengali via
  @fontsource only under `html:lang(bn)`, counts in local digits (`{{x, number}}`, `formatNumber`; IDs stay ASCII),
  CI guard tests (locale parity/placeholders/plurals; TS-compiler scan for literal JSX text and unknown keys),
  role names translated, product hint fixed at 360 px; 129 web tests; note 40; screenshot
- #24 Steadfast: paused and moved out of M3 (docs behind the merchant login, no sandbox; Syed's choice)

## Decisions waiting for Syed
- Bangla (#36): server error messages (ProblemDetails) are still English in both languages; translating them means
  error codes from the API (separate issue?). Times show "AM/PM" in Bangla (CLDR default); say if you prefer 24 h.
  Native date inputs follow the browser's locale.
- Paste chat (#35): a chat's name/address replaces a customer's auto-filled saved name/address (auto-fill is not
  "typed"); say if saved data should win. No per-field confirm before Save (20-second goal); revisit if AI drafts
  cause wrong orders. Server messages in the panel are English (same as other API errors).
- Chat-to-order (#34): not live-tested (no key yet): set `Ai:Provider` = Gemini and `Ai:ApiKey` in user-secrets and
  try a few real chats; Gemini's handling of the nullable JSON schema is the main unknown (fallback = notUnderstood).
  Names/addresses still go to Google, and the free tier may use prompts for training: a paid key (costs money,
  your call) before real customer chats. Several numbers in a chat -> the model picks one + `severalPhones`
  warning. Rate limit 6 drafts/shop/minute is a guess. Microsoft.Extensions.AI pinned to 10.x (11.x clashes with
  .NET 10's logging generator). PR is ~1,060 lines excl. generated (tests ~470).
- Dashboard/risk (#33): list risk counts all of a customer's orders (order detail still excludes the shown one).
  ForManyAsync is not EXPLAIN-checked at volume and there is no SQL query-count test yet (a small EF
  command-counting interceptor in the test factory would let us assert "2 queries per page"). Loss bars are
  rounded at the zero line (Recharts sets one radius per series). Flaky tests seen once each, both green on
  rerun: OrderListPage "reads the filters from a shared link" (web, under full-suite load) and one
  integration test I could not identify (my output filter dropped its name). Return-charge/receive-back
  buttons are still API-only.
- Money UI (#32): PR is ~1,450 lines (tests + MSW fake ~560, strings ~180), more than my 1,000 estimate.
  Paste expects the column order ID, collected, delivery fee, COD fee; real Pathao statement columns are
  unknown (check with a real export; a column-mapping step could follow). No payout list paging yet (latest
  20). Return-charge entry/receive-back still has no web button (#76 API only): add to #33 or a new issue?
- Risk (#29): default thresholds are my guess (Risky = 2+ returns and 50%+; Watch = 30%+, a return in the
  last 90 days, or 3+ cancellations). Check them with a few sellers. Cancellations count all cancels,
  including the seller's own (e.g. out of stock); a cancel reason would let us count only buyer-side ones.
  Order list rows have no risk yet (#33 needs a batched per-page query).
- Reports (#30): returned parcels cost only their return charge; a delivery fee the courier deducts on a
  returned parcel's payout line is not counted yet (follow-up if couriers charge both). Index benefit not
  benchmarked at realistic volume (tiny test DB plans via orders). Delivery day = our sync time.
- Export (#31): a failure mid-stream leaves a truncated file with HTTP 200 (headers already sent); fine for
  now, a background export job would fix it if shops grow huge. No web button yet.
- Returns (#76): re-match is manual (your choice); the
  button comes with #32. PR is ~610 lines excl. generated/migration (~700 with docs).
- **Docker (#28):** I started Docker Desktop and stopped (not removed) the 5 shopflow containers, as you
  chose. `docker start shopflow-control-plane shopflow-api-gateway shopflow-payment-service
  shopflow-order-service shopflow-notification-service` brings them back. Docker is still running.
- Payouts (#28): PR is ~1,300 lines excl. migration/generated (tests ~560). Fixing a mismatched payout
  needs #76 (re-match); there is no delete/undo, because CodSettled is terminal in the PRD lifecycle.
  A payout's `difference` is only shown (no claim/correction entry yet). Matching is exact on the
  trimmed consignment ID (case-sensitive). Net amount cannot be negative (courier invoices = later).
- COD summary (#27): plan features (COD tracking = Starter+ in the PRD) are not enforced: open an issue
  for a plan feature catalogue? The summary loads whole delivered orders (fine for MVP; scaling path in note 30).
  Buckets 0-3/4-7/8-14/15+ are my guess at courier payout rhythms: say if sellers think differently.
- Location picker (#71): bulk booking is still address-only. The "Delivery location" row on a booked
  shipment loads the courier's lists to show names; storing names on the shipment would avoid that
  (needs a migration). The picker always starts empty: pre-filling it from the customer's last
  booking would save packers time. Say if you want either.
- Invoices (#68): same permission as labels (Owner/Manager/Packer; Moderators cannot print); New and
  Confirmed orders can be invoiced. No web button yet: add it with #71 or separately? (small)
- **Pathao sandbox check (#66, PR #72):** add city-list, cities/{id}/zone-list, zones/{id}/area-list and
  merchant/price-plan to the #23 checklist (paths, `data.data` envelope, `home_delivery_available`,
  `final_price` / `cod_percentage` are from the community SDK, not a live sandbox).
- Locations (#66): ~1,040 lines excl. migration/generated (tests 449). Bulk booking stays address-only;
  cache is per instance (no single-flight). Say if you want an ADR for "destination on the Shipment".
- Courier UI (#26): PR is ~1,390 lines (one PR: Syed's choice; tests/fakes 503). Bulk bar wraps onto
  three rows at 390 px (5 actions); an overflow menu would tidy it. Bundle 199 kB gzip (code-splitting
  follow-up). Screenshots taken against a throwaway Postgres with process-only secrets (.env still absent).
- Labels (#25): long consignment IDs wrap on the thermal label (see screenshot); no barcode/QR yet (say
  if couriers or riders need one). One unexplained integration failure on the first local run (three
  re-runs green); watch CI on #69.
- Push: Git Credential Manager still has no GitHub login here (plain push hangs); I pushed with
  `gh auth git-credential` for that one command. `gh auth setup-git` fixes it for good.
- Steadfast (#24): resume when you have a Steadfast merchant account and can paste its API docs.
- **Pathao sandbox check (#23, PR #67):** Pathao's docs page was unreadable; field names, the order-info
  path and status codes come from a community SDK. Run the checklist in the PR with your sandbox keys
  (never commit them). Key unknowns: refresh_token in the token response, duplicate merchant_order_id
  behaviour, address-only bookings without city/zone.
- Pathao (#23): two API instances refreshing one connection at the same time could invalidate each
  other's refresh token (seller reconnects). Rare; a row lock on the refresh would fix it if needed.
- Bulk booking with an expired courier connection reports `courierUnavailable` per order (retry-safe,
  but the message says "did not answer"); a dedicated `courierAuthExpired` code would be clearer.
- Status sync (#22): a courier-side "Cancelled" only logs a warning (no Booked -> Cancelled move in the
  PRD). Staff need a place to see "needs review" parcels: a list filter or a web badge in #26?
- Status sync (#22): the partial tracking index is not yet checked with EXPLAIN at volume; Hangfire
  creates its own schema at startup (outside EF migrations). Fine for now; revisit before production.
- Bulk booking (#21): synchronous request (max 100 orders, 4 courier calls in flight, both in
  config). A Hangfire job with progress polling is the path for thousands of orders; say if needed.
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
- **Action needed (#19):** the API now needs `DataProtection:CertificateBase64` to start. Run the
  README "Data Protection certificate" lines once; I did not touch your user-secrets.
- Booking (#20): an order cancelled while its courier booking is in flight leaves a parcel at the
  courier (logged warning; #22 reconciles `Requested` shipments). Alternative: block cancel while a
  booking is in flight. PR is ~960 lines (half tests).
- Couriers (#19): Managers can connect couriers (the #8 matrix gives them ManageShop). Owner-only
  would need a new permission; say if you want it. (Pathao login: decided in #23, exchanged once.)
- Customer page (#59): blocking only warns, as since #13/#17 (orders are still allowed); say if blocked buyers
  should need an Owner override to order. PR is ~750 lines.
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
- 2026-11-20: #58 and #60 merged; #59 implemented, PR #61 open
- 2026-11-21: #61 merged (M2 complete); #19 as one PR with key ring in Postgres + certificate
  (Syed's choices); PR #62 open
- 2026-11-21: #62 merged; #20 with a Shipment entity and intent record + same key (Syed's
  choices); PR #63 open
- 2026-11-21: #63 merged; #21 implemented, PR #64 open (merged)
- 2026-11-21: #22 with a dispatcher, dashboard Development + local only, one PR (Syed's choices);
  PR #65 open (merged)
- 2026-11-21: #23 with the login exchanged once (Syed's choice); city/zone split to #66; PR #67 open (merged)
- 2026-11-21: #24 paused (Syed's choice); #25 with headless Chromium (Syed's choice, ADR 0008),
  invoices split to #68; waiting on the Chromium install
- 2026-11-22: Chromium installed; #25 tested end to end with Docker, label screenshot added; PR #69 open (merged)
- 2026-11-22: #26 as one PR, labels downloaded (Syed's choices); PR #70 open (merged)
- 2026-11-22: #66 destination on the Shipment, backend now + web split to #71 (Syed's choices); PR #72 open (merged)
- 2026-11-22: #68 implemented (A5 default, CanPack, routine choices); PR #73 open
- 2026-11-24: #75 merged; #28 with settle-and-flag short payments, mismatches saved, returns split to #76
  (Syed's choices); PR #77 open
- 2026-11-24: #77 merged; #29 with a New level, risk on lookup/customer/order detail, server-wide
  thresholds (Syed's choices); PR #78 open
- 2026-11-24: #78 merged; #76 before #30 (profit needs return charges), charge settable later and manual
  re-match (Syed's choices); PR #79 open
- 2026-11-24: #79 merged; #30 with delivery-day sales, unknowns as 0 + missing counts, tenant_id on status
  history (Syed's choices); PR #80 open
- 2026-11-24: #80 merged; #31 Owner-only export, one row per order (Syed's choices), hand-written CSV writer
  (no library); PR #81 open
- 2026-11-24: #81 merged; #32 with statement paste, one PR (Syed's choices); PR #82 open
- 2026-11-24: #82 merged (GitHub glitch, closed by hand); #33 as one PR, Owner-only dashboard (Syed's choices); PR #83 open
- 2026-11-26: #83 merged; #34 built without a key, Google.GenAI official SDK (Syed's choices); PR #84 open
- 2026-11-26: #84 merged; #35 implemented (routine choices), PR #85 open
- 2026-11-26: #85 merged; #36 implemented (@fontsource font, Bangla default: Syed's choices), PR #86 open
