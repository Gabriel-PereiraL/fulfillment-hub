# ADR-003 — Simulated external providers

**Status**: accepted · **Date**: 2026-09-18

## Context
The product integrates with a delivery provider and a payment provider. There are (and must be) no real credentials,
deliveries or charges. We need to exercise resilience, idempotency and webhooks under controlled failure scenarios.

## Options
1. Call a real sandbox (Uber Direct test mode) — requires an account, credentials, terms; no control over failures; risk of confusing a portfolio with a commercial integration.
2. Mocks in tests only (no real process) — does not exercise the network, timeouts, real webhooks.
3. **Our own simulator in .NET** reproducing a documented subset of the public contract, with configurable scenarios.

## Decision
Option 3. `FulfillmentHub.ProviderSimulator` hosts `/delivery/v1` (a subset of the Uber Direct API: token, quote, create, get,
cancel, errors, signed `event.delivery_status` webhook) and `/payments/v1` (our own minimal contract). Scenarios through
variables (`SIM_*`). Mandatory documentation of the real vs. simulated contract in `INTEGRATIONS.md`, with sources and dates.

## Rationale
Full control over failures (latency, 429, 5xx, timeouts, duplicated/out-of-order webhooks), reproducibility in tests and CI,
zero legal/financial risk, and more .NET code to demonstrate (the simulator is backend too).

## Trade-offs
- The simulator may diverge from the real provider; mitigation: the "REAL vs SIMULATOR" table and contract tests against the official OpenAPI spec.
- It does not prove a commercial integration — and the project **will never claim** that.

## Consequences
- Fixed disclaimer in the README, the docs and the simulator itself: "This project does not connect to Uber infrastructure…".
- `IDeliveryProviderClient` is the single port; a second provider (BL-069) is possible without touching the domain.
