# Project Status — FulfillmentHub

Last update: 2026-09-18.

## Current status

- Completed phases: **0–9** of 20 (see [ROADMAP.md](ROADMAP.md) for the gate criteria and results of each phase).
- Current stable milestone: **Messaging / SQS** — an order travels `Created → AwaitingPayment → Paid → DeliveryRequested → InDelivery → Delivered` end to end, with external effects leaving through the transactional outbox and SQS queues (LocalStack locally) and the simulated providers answering through signed webhooks.
- Next phase: **10 — Security hardening** (not started).
- Tests: **225** green (125 unit, 5 architecture, 95 integration; the integration tests run against real PostgreSQL and LocalStack containers via Testcontainers).
- Build: 0 warnings (`TreatWarningsAsErrors`); `dotnet format` clean; 0 vulnerable packages (`dotnet list package --vulnerable --include-transitive`).

## Completed capabilities

| Area | What exists |
|---|---|
| Core architecture | Modular monolith (`Domain`, `Application`, `Infrastructure`) with three hosts: `Api`, `Worker`, `ProviderSimulator`; dependency direction enforced by architecture tests (ADR-001, ADR-006) |
| Persistence | PostgreSQL 17 + EF Core 10 (Npgsql): snake_case schema, complex types, strongly-typed IDs, `xmin` optimistic concurrency, partial unique indexes, 6 migrations (ADR-002) |
| Authentication and authorization | Own users, `PasswordHasher`, JWT HS256, deny-by-default `FallbackPolicy`, per-role policies, resource authorization, login rate limiting, indistinguishable login failures (ADR-008) |
| Orders | `POST /orders` idempotent by `Idempotency-Key` (ADR-010), stock reservation with optimistic concurrency, delivery quote at checkout with an estimated fee as fallback, cancellation with stock return, keyset pagination |
| Payments | Simulated provider; typed `HttpClient` with timeout → retry with backoff/jitter → circuit breaker; HMAC-signed webhooks with inbox deduplication and `GET` verification before applying `paid`; reconciliation of pending payments; automatic refund of orders cancelled after capture |
| Deliveries | "Uber-like" simulator (documented subset of the public contract); token cache with renewal; requote on `expired_quote`; `409 duplicate_delivery` reconciled by adoption; out-of-order/duplicate/delayed events applied by rule; reconciliation of silent deliveries |
| Signed webhooks | Shared `WebhookReceiver` pipeline: body limit, constant-time HMAC-SHA256, timestamp window, inbox with `UNIQUE(provider, provider_event_id)`, always-200 acknowledgement, rate limiting |
| Transactional outbox | Domain events written in the same `SaveChanges` as the aggregate; `FOR UPDATE SKIP LOCKED` + lease publisher; exponential backoff; `Failed` after N attempts; admin endpoints to list and requeue (ADR-004) |
| SQS messaging | `fh-domain-events` and `fh-webhooks-inbound` queues with dead-letter queues; consumers with long polling, bounded concurrency, visibility backoff and deduplication persisted in the same transaction as the effect; in-process mode when the broker is disabled (ADR-005) |
| Observability foundation | Structured logging with `LoggerMessage` (no PII/secrets), OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, AWS SDK, runtime), custom spans and `fh.*` metrics, trace propagation through the outbox and the queue, correlation id, Aspire Dashboard locally (ADR-009) |

Per-guarantee test evidence (idempotency, concurrency, outbox zero-loss, DLQ, redelivery, …) is listed in the matrix of [TEST_STRATEGY.md](TEST_STRATEGY.md) §3.

## Not implemented yet

Security hardening (headers, CORS, OWASP checklist with evidence), observability hardening (runbooks, alerts), E2E/contract/chaos tests, Docker images and the full compose, CI, Terraform/AWS deployment, the Admin/Ops UI (Blazor) and performance tests. None of these are claimed as done anywhere in the documentation.

## Next

- Phase plan, gates and acceptance criteria: [ROADMAP.md](ROADMAP.md).
- Open items by priority: [BACKLOG.md](BACKLOG.md).
- Decisions taken and pending: [DECISIONS.md](DECISIONS.md).
