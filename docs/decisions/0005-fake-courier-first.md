# 0005: Fake courier first, then the Pathao sandbox

- Status: Accepted
- Date: 2026-11-19

## Context

Booking a courier is in the core flow (create order -> book -> delivered -> COD settled).
Real courier APIs need merchant or sandbox credentials, and must never be called from
tests or CI.

## Decision

1. Build `ICourierProvider` with a **Fake courier** adapter first. It returns
   deterministic consignment IDs and lets tests and the demo move parcels through
   statuses.
2. Add the **Pathao sandbox** adapter next, tested with WireMock.Net contract tests;
   the real sandbox is only used manually with Syed's own sandbox credentials.
3. Steadfast follows the same pattern.

## Alternatives considered

| Option | Trade-off |
| --- | --- |
| Fake courier only for MVP | No accounts needed, but the showcase has no real integration. |
| Pathao sandbox first | Real integration early, but blocked until credentials arrive, and e2e tests would depend on an external service. |

## Consequences

- The full flow, e2e tests and the demo work with no API keys.
- The interface is designed before a real API shapes it, so the Pathao adapter may push
  small changes to `ICourierProvider`; contract tests make that safe.
