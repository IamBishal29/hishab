# 0007: Courier credentials: Data Protection, key ring in Postgres, wrapped by a certificate

- Status: Accepted
- Date: 2026-11-20

## Context

Sellers connect their courier accounts with their own API keys (PRD, "Courier booking").
Hishab must be able to read those keys to call the courier, so they cannot be hashed; they
must be encrypted at rest. The question is where the keys that decrypt them live, because
that decides what a leaked database dump or backup exposes.

## Decision

1. Encrypt courier credentials with **ASP.NET Core Data Protection** (authenticated
   encryption, automatic key rotation every 90 days, old keys kept for decryption).
2. Persist the **key ring in Postgres** (`data_protection_keys`, via
   `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`), so several API instances share
   it and it is backed up with the data it decrypts.
3. **Wrap every key in the ring with an X.509 certificate** (`ProtectKeysWithCertificate`)
   loaded from configuration (`DataProtection:CertificateBase64`, a base64 PFX, from
   user-secrets or environment variables; never committed). The database alone cannot
   decrypt anything.
4. Bind each ciphertext to its shop: the protector purpose includes the tenant ID, so a
   ciphertext copied into another shop's row fails to decrypt.
5. The API never returns credentials; it shows a masked hint (`••••a9f2`).

## Alternatives considered

| Option | Trade-off |
| --- | --- |
| Key ring in Postgres only | Simplest, but keys sit next to the ciphertext: one dump exposes every seller's courier keys. |
| Key ring on a file system volume | A DB dump alone is safe, but single instance only, and losing the volume makes every stored key unreadable. |
| Cloud KMS / Key Vault | Best key custody, but needs a cloud account (guardrail: free tools only). Can replace the certificate later (`ProtectKeysWithAzureKeyVault` or similar) without touching courier code. |

## Consequences

- One more required secret at startup (validated with `ValidateOnStart`); README explains how to
  create a development certificate.
- Losing the certificate makes stored courier keys unreadable: sellers reconnect their couriers.
  Back the certificate up like any other production secret.
- Rotating the certificate: add the new one, keep the old one available for unwrapping
  (`UnprotectKeysWithAnyCertificate`) until old keys expire.
