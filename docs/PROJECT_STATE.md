# Project Status — FulfillmentHub

Last update: 2026-09-21.

## Current status

- Completed phases: **0–10** of 20, plus the *alerting* subset of Phase 11 and the *CI + SAST* subset of Phase 14,
  executed together as the cross-phase hardening track (see [ROADMAP.md](ROADMAP.md), "Hardening track", and D-77).
- Current stable milestone: **Security hardening + local alerting** — an order travels `Created → AwaitingPayment → Paid
  → DeliveryRequested → InDelivery → Delivered` end to end (outbox, SQS via LocalStack, signed webhooks), the API ships
  security headers, per-user order rate limiting and a body limit, and five alert rules evaluated on the application's
  real metrics fire on the local Grafana stack (four of them provoked and observed firing and resolving on 2026-09-21).
- Phases still open: **11 — Observability hardening** (use-case spans, remaining metrics, trace-id test) and
  **14 — CI** (image build/scan; the build/test and CodeQL workflows ran on GitHub on 2026-09-21 — CodeQL green with
  1 finding triaged, CI green after a timing fix in a test poller).
- Tests: **231** green (125 unit, 6 architecture, 100 integration; the integration tests run against real PostgreSQL and
  LocalStack containers via Testcontainers).
- Build: 0 warnings (`TreatWarningsAsErrors`); `dotnet format` clean; 0 vulnerable packages
  (`dotnet list package --vulnerable --include-transitive`); workflows linted with `actionlint` (0 errors).

## Completed capabilities

| Area | What exists |
|---|---|
| Core architecture | Modular monolith (`Domain`, `Application`, `Infrastructure`) with three hosts: `Api`, `Worker`, `ProviderSimulator`; dependency direction enforced by architecture tests (ADR-001, ADR-006) |
| Persistence | PostgreSQL 17 + EF Core 10 (Npgsql): snake_case schema, complex types, strongly-typed IDs, `xmin` optimistic concurrency, partial unique indexes, 6 migrations (ADR-002) |
| Authentication and authorization | Own users, `PasswordHasher`, JWT HS256, deny-by-default `FallbackPolicy`, per-role policies, resource authorization, login rate limiting, indistinguishable login failures (ADR-008) |
| Security hardening (Phase 10) | Threat model with assets, actors, trust boundaries, STRIDE table with status per threat, dispositions of every previously "planned" item, residual risks and OWASP Top 10 with evidence ([SECURITY.md](SECURITY.md)); security headers + HSTS outside Development, `POST /orders` sliding-window limit per user, Kestrel 256 KB body limit, guard test that no log message carries PII/secrets; CORS deliberately not enabled and HTTPS termination left to the edge (D-82) |
| Orders | `POST /orders` idempotent by `Idempotency-Key` (ADR-010), stock reservation with optimistic concurrency, delivery quote at checkout with an estimated fee as fallback, cancellation with stock return, keyset pagination |
| Payments | Simulated provider; typed `HttpClient` with timeout → retry with backoff/jitter → circuit breaker; HMAC-signed webhooks with inbox deduplication and `GET` verification before applying `paid`; reconciliation of pending payments; automatic refund of orders cancelled after capture |
| Deliveries | "Uber-like" simulator (documented subset of the public contract); token cache with renewal; requote on `expired_quote`; `409 duplicate_delivery` reconciled by adoption; out-of-order/duplicate/delayed events applied by rule; reconciliation of silent deliveries |
| Signed webhooks | Shared `WebhookReceiver` pipeline: body limit, constant-time HMAC-SHA256, timestamp window, inbox with `UNIQUE(provider, provider_event_id)`, always-200 acknowledgement, rate limiting |
| Transactional outbox | Domain events written in the same `SaveChanges` as the aggregate; `FOR UPDATE SKIP LOCKED` + lease publisher; exponential backoff; `Failed` after N attempts; admin endpoints to list and requeue (ADR-004) |
| SQS messaging | `fh-domain-events` and `fh-webhooks-inbound` queues with dead-letter queues; consumers with long polling, bounded concurrency, visibility backoff and deduplication persisted in the same transaction as the effect; in-process mode when the broker is disabled (ADR-005) |
| Observability | Structured logging with `LoggerMessage` (no PII/secrets), OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, AWS SDK, runtime), custom spans and `fh.*` metrics, trace propagation through the outbox and the queue, correlation id (ADR-009) |
| Local observability backend + alerting (Phase 11 subset) | `grafana/otel-lgtm` in the compose (Prometheus, Tempo, Loki, Grafana) with a provisioned dashboard and five alert rules (`ApiHigh5xxRate`, `ApiHighLatencyP95`, `WorkerHeartbeatMissing`, `OutboxBacklog`, `QueueDlqNotEmpty`); reproducible fault scenarios with existing knobs; drills executed with evidence in [incidents/](incidents/) (ADR-009 addendum, D-78/D-79/D-80) |
| CI / SAST (Phase 14 subset) | `.github/workflows/ci.yml` (restore, build with analyzers, format, vulnerable-package gate, full test suite with Testcontainers, dependency review, gitleaks) and `codeql.yml` (CodeQL C#, `security-extended`) — executed on GitHub Actions on 2026-09-21 (CodeQL: 63 rules, 1 finding triaged as false positive, 0 open; CI green after the test-poller fix) ([DEPLOYMENT.md §4.1](DEPLOYMENT.md)) |

Per-guarantee test evidence (idempotency, concurrency, outbox zero-loss, DLQ, redelivery, …) is listed in the matrix of [TEST_STRATEGY.md](TEST_STRATEGY.md) §3.

## Not implemented yet

Remaining observability hardening (use-case spans, `fh.idempotency.hits`, trace-id test), E2E/contract/chaos tests,
Docker images and the full compose, image build/scan in CI, Terraform/AWS deployment (including CloudWatch alarms and
`ForwardedHeaders`/TLS at the load balancer), the Admin/Ops UI (Blazor) and performance tests. None of these are claimed as done anywhere in the documentation.

## Next

- Phase plan, gates and acceptance criteria: [ROADMAP.md](ROADMAP.md).
- Open items by priority: [BACKLOG.md](BACKLOG.md).
- Decisions taken and pending: [DECISIONS.md](DECISIONS.md).
