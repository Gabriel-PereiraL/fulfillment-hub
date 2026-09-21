#!/usr/bin/env bash
# Runs the black-box end-to-end tests (tests/FulfillmentHub.E2ETests) against a RUNNING stack: API, Worker and provider
# simulator (started with `dotnet run`, or the containers of the compose "app" profile), with the development seed
# applied. docs/TEST_STRATEGY.md T23, docs/DEVELOPMENT.md §3.
#
# Usage: scripts/run-e2e.sh
# Env:   FH_E2E_API_URL (default http://localhost:5000)
#        FH_E2E_CUSTOMER_EMAIL (default customer@fulfillmenthub.local)
#        FH_E2E_CUSTOMER_PASSWORD (default: read from the Api user-secrets key Seed:CustomerPassword)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
export FH_E2E_API_URL="${FH_E2E_API_URL:-http://localhost:5000}"
export FH_E2E_CUSTOMER_EMAIL="${FH_E2E_CUSTOMER_EMAIL:-customer@fulfillmenthub.local}"

if [[ -z "${FH_E2E_CUSTOMER_PASSWORD:-}" ]]; then
  FH_E2E_CUSTOMER_PASSWORD="$(dotnet user-secrets list --project "$ROOT/src/FulfillmentHub.Api" | sed -n 's/^Seed:CustomerPassword = //p')"
  export FH_E2E_CUSTOMER_PASSWORD
fi
if [[ -z "$FH_E2E_CUSTOMER_PASSWORD" ]]; then
  echo "FH_E2E_CUSTOMER_PASSWORD not set and Seed:CustomerPassword not found in user-secrets" >&2
  exit 1
fi

if ! curl -sf -m 5 "$FH_E2E_API_URL/health/ready" >/dev/null; then
  echo "API not ready at $FH_E2E_API_URL/health/ready — start the stack first (docs/DEVELOPMENT.md §2)" >&2
  exit 1
fi

echo "E2E against $FH_E2E_API_URL as $FH_E2E_CUSTOMER_EMAIL"
exec dotnet test --project "$ROOT/tests/FulfillmentHub.E2ETests" "$@"
