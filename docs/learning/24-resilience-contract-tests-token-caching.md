# 24. Calling a third party: resilience pipelines, contract tests and token caching

## The idea in plain words
Calling a courier's API is like ringing a busy call centre:
- **Timeout:** hang up after 10 seconds instead of waiting forever.
- **Retry with backoff:** call again a little later, then later still. Only for *questions*
  ("where is parcel X?"). Never re-place an *order* automatically: they may have taken it even
  though the line went dead.
- **Circuit breaker:** after half the calls in 30 s have failed, stop calling for 30 s. The call
  centre gets a rest, and our users get "try again" straight away instead of a 30 s spinner.
- **Token caching:** an access token is a visitor badge. Keep it until it nearly expires, and get
  the next one with the refresh token, never with the password.
- **Contract tests:** a pretend call centre (WireMock) that answers exactly the way the real one
  does, so tests and CI never call the real courier.

```mermaid
flowchart LR
  A[Adapter call] --> T{token valid?}
  T -- yes --> P[HTTP pipeline: total timeout, retry (GET only), circuit breaker, attempt timeout]
  T -- no --> R[refresh once per connection, single-flight] --> S[save rotated tokens, encrypted] --> P
  P -- 401 --> R2[refresh once, resend]
  P -- 5xx/timeout/open circuit --> U[CourierUnavailableException: retry later is safe]
  P -- 422 --> X[BookingResult.Rejected: Pathao's reason]
```

## Why we built it this way
- **The password is exchanged, never stored.** At connect time the merchant login goes to
  Pathao's token endpoint once. We keep only the encrypted tokens (`CredentialCheck.ValidKeeping`).
  If the refresh token stops working, the seller reconnects (`CourierAuthExpiredException`, 422).
- **Rotated refresh tokens must be saved.** Pathao may issue a new refresh token on every
  refresh, and the old one may stop working. The adapter saves through a hook that opens its own
  scope (bulk booking calls run in parallel, and a request's DbContext is not thread-safe).
  Refreshes are single-flight per connection, so five parallel calls trigger one refresh, not five
  competing ones.
- **No automatic retry for POST.** `DisableForUnsafeHttpMethods()` keeps Polly from re-sending a
  booking. Idempotency lives one level up: the intent record plus the same merchant reference
  (note 21). A 401 is resent once, because a 401 means nothing was done.
- **`IHttpClientFactory`, not a stored `HttpClient`.** The adapter is a singleton. Holding one
  `HttpClient` forever misses DNS changes, while the factory rotates handlers.
- **Testing lesson:** Polly waits using the `TimeProvider` from DI. Registering a fake clock that
  never advances froze every retry backoff, and the test hung. Fake time only where you also
  advance it.

## Interview questions
1. **How do you test a third-party integration without calling it?**
   Contract tests against a local fake server (WireMock) that returns recorded or documented
   responses: success, validation error, 401, 5xx and malformed bodies. They run the real HTTP
   pipeline, and the base URL comes from config, so CI can never reach production. A
   documented manual sandbox check covers what the fake can't know.
2. **When should a circuit breaker open, and what do callers see?**
   When the failure ratio over a sampling window passes a threshold, with a minimum number of
   calls so one failure doesn't trip it. While it is open, calls fail at once (we map that to
   503 "try again"), and after the break one trial call decides whether to close it. Count
   only real failures (5xx, timeouts), not 4xx.
3. **Why not retry a timed-out POST?**
   A timeout tells you nothing about whether the server acted on the request. Retrying a
   non-idempotent call can book two parcels. Retry only safe methods, and make writes
   idempotent with a client-generated key the server or your own records can check.
