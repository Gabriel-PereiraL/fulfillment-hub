# ADR-010 — Idempotency strategy

**Status**: accepted · **Date**: 2026-09-18 (implementation: Phases 4–9)

## Context
Repetitions happen: a client resends `POST /orders` after a timeout; a provider resends a webhook; our call to the provider is
repeated by the retry; the queue delivers the same message twice. None of them may produce two orders, two payments,
two deliveries or two side effects.

## Decision (four boundaries)

| Boundary | Mechanism | Key | Behaviour |
|---|---|---|---|
| **API (inbound)** | mandatory `Idempotency-Key` header on mutating POSTs; endpoint filter + `idempotency_records` table (`PK(scope, key)`, `request_hash`, `status`, stored response, `expires_at` 24 h) | client-generated UUID, scope = user | same key + same hash → stored response (same status/body); same key + different hash → `422`; key `InProgress` → `409`; expired → new execution |
| **Webhooks (inbound)** | `INSERT` into `webhook_events` with `UNIQUE(provider, provider_event_id)` **before** any effect | the provider's event id | duplicate → `200` without reprocessing; metric |
| **Providers (outbound)** | `idempotency_key` on `POST deliveries`; `Idempotency-Key` on `POST payments`; derived from the `OrderId` (+ requote attempt) | deterministic per order | retry/timeout creates no duplicate; `409 duplicate_delivery` → `GET` and reconcile |
| **Consumers (outbox/SQS)** | `processed_messages (consumer, message_id)` in the same commit as the effect; idempotent state transitions (same state = no-op) | message id | second delivery → no-op |

Complement: uniqueness constraints in the domain (one active payment per order, one active delivery per order) as the last line of defence.

## Rationale
Each boundary has different semantics; a single "magic table" does not cover them all. Persisting the key **before** the effect and in the
same commit is what makes the guarantee real, not best-effort.

## Trade-offs
- Storing responses takes space (jsonb + 24 h TTL; purge job P2).
- Requiring an `Idempotency-Key` complicates simple clients (accepted; it is standard practice in payment/logistics APIs).
- The body hash needs canonical serialization (use the raw request bytes).

## Consequences
- Tests T1–T3, T6, T9 (timeout with an idempotency key), T14 in the TEST_STRATEGY.md matrix.
- OpenAPI documents the header; specific ProblemDetails for the idempotency 409/422.

## Implementation (Phase 4, 2026-09-18) — refinements over the decision
- **Fingerprint** = SHA-256 of `METHOD PATH\n` + the canonical JSON of the **already bound** request (`JsonSerializer` with the API options), not of the raw bytes: the endpoint filter runs after binding (the body has already been consumed) and the canonical JSON ignores irrelevant whitespace/property-order differences.
- **What is stored and replayed**: any response with status < 500 (including 4xx such as `409 insufficient_stock`) — the key represents *that* order; to try again the client uses another key. On an exception/5xx the key is released (`ReleaseAsync`) to allow a retry with the same key.
- **Replay**: status, body, `Content-Type`, `Location` and the `Idempotent-Replayed: true` header.
- **Scope** = the authenticated user's id; key 1–64 chars `[A-Za-z0-9-_]`; 24 h TTL (an expired row is reused by a new request); purging expired rows is P2.
- **Its own unit of work** (`IdempotencyStore` with a separate DI scope): the key is claimed before the handler and finalized after it, independently of the request's `DbContext` — a collision between concurrent calls with the same key is resolved by the `(scope, key)` primary key.
- `POST /orders/{id}/cancel` does not require a key: cancellation is naturally idempotent (same state = no-op).
