# 0006: One shop per user

- Status: Accepted
- Date: 2026-11-19

## Context

Sellers sign in with their phone number (#5). Staff are invited by phone (#8). The JWT
carries one `tenant_id` (#6). We must decide whether one phone number (user) can belong
to several shops.

## Decision

A user belongs to **exactly one shop**. The user row stores its `TenantId` and role, the
phone number is unique across all users, and the JWT `tenant_id` is the user's shop.
This matches the PRD ("one account = one tenant").

## Alternatives considered

| Option | Trade-off |
| --- | --- |
| Memberships (user <-> many shops) | Supports staff who work for two shops and owners with two brands, but needs a shop picker at login, a membership table, and more tests now. Multiple brands are a "Later" feature in the PRD. |

## Consequences

- Login is simple: phone -> OTP -> the user's shop.
- A person working for two shops needs a second phone number (rare in the target
  segment).
- Moving to memberships later is additive: create a `ShopMembership` row per existing
  user, then read the tenant from the chosen membership instead of the user row.
