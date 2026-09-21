#!/usr/bin/env bash
# Prints the state of the provisioned FulfillmentHub alert rules from the local Grafana (grafana/otel-lgtm) and the
# last evaluated value. Uses the Grafana ruler API (anonymous admin is enabled in the local container only).
#
# Usage: scripts/alerts-status.sh            # one snapshot
#        scripts/alerts-status.sh --watch    # refresh every 10 s until Ctrl+C
# Env:   GRAFANA_URL (default http://localhost:3000)
set -euo pipefail

GRAFANA_URL="${GRAFANA_URL:-http://localhost:3000}"

snapshot() {
  local json
  json="$(curl -sf "$GRAFANA_URL/api/prometheus/grafana/api/v1/rules")" || { echo "Grafana not reachable at $GRAFANA_URL" >&2; return 1; }
  printf '%s  %-24s %-10s %s\n' "$(date +%H:%M:%S)" "RULE" "STATE" "DETAIL"
  # One line per rule: name, state (inactive|pending|firing), health, evaluated value. Rules are split on their leading
  # {"state":"…","name":"…" pair (alert instances have a state too, but no name right after it).
  echo "$json" | perl -0ne '
    for my $r (split /(?=\{"state":"\w+","name":")/) {
      next unless $r =~ /^\{"state":"(\w+)","name":"([^"]+)"/;
      my ($state, $name) = ($1, $2);
      my $health = ($r =~ /"health":"(\w+)"/) ? $1 : "?";
      my $value  = ($r =~ /"value":"([^"]+)"/) ? "value=$1" : "";
      printf "          %-24s %-10s health=%s %s\n", $name, $state, $health, $value;
    }'
}

if [[ "${1:-}" == "--watch" ]]; then
  while true; do snapshot; echo; sleep 10; done
else
  snapshot
fi
