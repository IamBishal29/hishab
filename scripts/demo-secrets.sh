#!/usr/bin/env bash
# Prints fresh secrets for the compose demo (docker compose --profile app). Append them to .env:
#   scripts/demo-secrets.sh >> .env
# Nothing is written anywhere else; the temporary certificate files are deleted on exit.
set -euo pipefail

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Data Protection key-ring certificate (ADR 0007): RSA, no password, base64 PFX. Relative file names
# inside the temp folder: MSYS_NO_PATHCONV (Git Bash, for -subj) would otherwise break absolute paths.
(
  cd "$tmp"
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 730     -subj "/CN=hishab-demo-data-protection" -keyout dp.key -out dp.crt 2>/dev/null
  openssl pkcs12 -export -inkey dp.key -in dp.crt -out dp.pfx -passout pass:
)

echo "OTP_HASH_KEY=$(openssl rand -base64 32)"
echo "JWT_SIGNING_KEY=$(openssl rand -base64 32)"
echo "DATA_PROTECTION_CERT_BASE64=$(base64 -w0 "$tmp/dp.pfx" 2>/dev/null || base64 -i "$tmp/dp.pfx" | tr -d '\n')"
