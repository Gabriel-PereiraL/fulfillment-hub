#!/usr/bin/env bash
# Places N orders through the real API as the seeded development customer, to generate traffic for the local
# observability drills (docs/OBSERVABILITY.md §8). Development only: needs the API on $API_URL and the seed applied.
#
# Usage: scripts/place-orders.sh [count=20] [pause_seconds=0.5]
# Env:   API_URL (default http://localhost:5000), CUSTOMER_EMAIL (default customer@fulfillmenthub.local),
#        CUSTOMER_PASSWORD (default: read from the Api user-secrets key Seed:CustomerPassword)
set -euo pipefail

COUNT="${1:-20}"
PAUSE="${2:-0.5}"
API_URL="${API_URL:-http://localhost:5000}"
CUSTOMER_EMAIL="${CUSTOMER_EMAIL:-customer@fulfillmenthub.local}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if [[ -z "${CUSTOMER_PASSWORD:-}" ]]; then
  CUSTOMER_PASSWORD="$(dotnet user-secrets list --project "$ROOT/src/FulfillmentHub.Api" | sed -n 's/^Seed:CustomerPassword = //p')"
fi
if [[ -z "$CUSTOMER_PASSWORD" ]]; then
  echo "CUSTOMER_PASSWORD not set and Seed:CustomerPassword not found in user-secrets" >&2
  exit 1
fi

token="$(curl -sf "$API_URL/api/v1/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$CUSTOMER_EMAIL\",\"password\":\"$CUSTOMER_PASSWORD\"}" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')"
[[ -n "$token" ]] || { echo "login failed" >&2; exit 1; }

product_id="$(curl -sf "$API_URL/api/v1/products" -H "Authorization: Bearer $token" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)"
[[ -n "$product_id" ]] || { echo "no product found (run: dotnet run --project src/FulfillmentHub.Api -- seed)" >&2; exit 1; }

body="$(cat <<JSON
{"items":[{"productId":"$product_id","quantity":1}],
 "deliveryAddress":{"street":"Rua das Flores","number":"123","district":"Centro","city":"Recife","state":"PE","postalCode":"50000-000","country":"BR"}}
JSON
)"

declare -A statuses
for ((i = 1; i <= COUNT; i++)); do
  start=$(date +%s%N)
  status="$(curl -s -o /dev/null -w '%{http_code}' "$API_URL/api/v1/orders" \
    -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: drill-$(date +%s%N)-$i" -d "$body")"
  ms=$(( ($(date +%s%N) - start) / 1000000 ))
  statuses[$status]=$(( ${statuses[$status]:-0} + 1 ))
  printf '%3d  POST /api/v1/orders -> %s  (%d ms)\n' "$i" "$status" "$ms"
  sleep "$PAUSE"
done

echo "--- summary ---"
for s in "${!statuses[@]}"; do echo "HTTP $s: ${statuses[$s]}"; done
