# Project Status — FulfillmentHub

Last update: 2026-09-21.

## Current status

- Completed phases: **0–14** of 20 (see [ROADMAP.md](ROADMAP.md) for the gate criteria and results of each phase).
- Current stable milestone: **containerised system with CI, SAST, alerting and end-to-end tests** — an order travels
  `Created → AwaitingPayment → Paid → DeliveryRequested → InDelivery → Delivered` end to end (outbox, SQS via LocalStack,
  signed webhooks); the whole system runs from `docker compose --profile deps --profile app up --build` and the black-box
  E2E flows pass against it; alert rules fire from the application's real metrics on the local Grafana stack; GitHub
  Actions runs the test suite, CodeQL, image build, Trivy and the E2E flows.
- Next phase: **15 — AWS IaC (Terraform)** — requires an AWS account and a cost decision from the owner (blocker outside
  the code). Phases 15–16 are the only ones left before the Admin UI (17), performance tests (18) and the documentation
  and release phases (19–20).
- Tests: **248** (137 unit, 6 architecture, 102 integration against real PostgreSQL and LocalStack containers via
  Testcontainers, 3 black-box E2E that skip unless a stack is running; E2E 3/3 green on 2026-09-21 against both `dotnet run`
  hosts and the containers).
- Build: 0 warnings (`TreatWarningsAsErrors`); `dotnet format` clean; 0 vulnerable packages
  (`dotnet list package --vulnerable --include-transitive`, locked restore); images 168–209 MB with Trivy 0 HIGH/CRITICAL;
  workflows linted with `actionlint`.
- Remote gate of Phases 11–14 closed on 2026-09-21: CI run 35623513605 (locked restore, 245 tests, images 144/117/130 MB on
  the runner, Trivy 0 HIGH/CRITICAL, E2E 3/3 against the containers, clean teardown) and CodeQL run 35623513653 green.

## Completed capabilities

| Area | What exists |
|---|---|
| Core architecture | Modular monolith (`Domain`, `Application`, `Infrastructure`) with three hosts: `Api`, `Worker`, `ProviderSimulator`; dependency direction enforced by architecture tests (ADR-001, ADR-006) |
| Persistence | PostgreSQL 17 + EF Core 10 (Npgsql): snake_case schema, complex types, strongly-typed IDs, `xmin` optimistic concurrency, partial unique indexes, 6 migrations; explicit one-off `migrate` command (ADR-002) |
| Authentication and authorization | Own users, `PasswordHasher`, JWT HS256, deny-by-default `FallbackPolicy`, per-role policies, resource authorization, login rate limiting, indistinguishable login failures (ADR-008) |
| Security hardening (Phase 10) | Threat model with assets, actors, trust boundaries, STRIDE table with status per threat, dispositions, residual risks and OWASP Top 10 with evidence ([SECURITY.md](SECURITY.md)); security headers + HSTS outside Development, `POST /orders` sliding-window limit per user, Kestrel 256 KB body limit, guard test that no log message carries PII/secrets; CORS deliberately not enabled (D-82) |
| Orders | `POST /orders` idempotent by `Idempotency-Key` (ADR-010), stock reservation with optimistic concurrency, delivery quote at checkout with an estimated fee as fallback, cancellation with stock return and concurrency retry (D-88), keyset pagination |
| Payments | Simulated provider; typed `HttpClient` with timeout → retry with backoff/jitter → circuit breaker; HMAC-signed webhooks with inbox deduplication and `GET` verification before applying `paid`; reconciliation of pending payments; automatic refund of orders cancelled after capture |
| Deliveries | "Uber-like" simulator (documented subset of the public contract); token cache with renewal; requote on `expired_quote`; `409 duplicate_delivery` reconciled by adoption; out-of-order/duplicate/delayed events applied by rule; reconciliation of silent deliveries |
| Signed webhooks | Shared `WebhookReceiver` pipeline: body limit, constant-time HMAC-SHA256, timestamp window, inbox with `UNIQUE(provider, provider_event_id)`, always-200 acknowledgement, rate limiting |
| Transactional outbox | Domain events written in the same `SaveChanges` as the aggregate; `FOR UPDATE SKIP LOCKED` + lease publisher; exponential backoff; `Failed` after N attempts; admin endpoints to list and requeue (ADR-004) |
| SQS messaging | `fh-domain-events` and `fh-webhooks-inbound` queues with dead-letter queues; consumers with long polling, bounded concurrency, visibility backoff and deduplication persisted in the same transaction as the effect; in-process mode when the broker is disabled (ADR-005) |
| Observability (Phases 1–11) | Structured logging with `LoggerMessage` (no PII/secrets), OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, AWS SDK, runtime), use-case/provider/outbox/queue/webhook spans with one trace id from the request to the provider call, the full `fh.*` metric catalog, correlation id (ADR-009); `grafana/otel-lgtm` locally with a provisioned dashboard and five alert rules, four provoked and observed firing/resolving ([incidents/](incidents/)) |
| Testing hardening (Phase 12) | Black-box E2E project (3 flows), provider contract tests (11 pairs + negative self-check), chaos convergence test (30 % provider failures), trace-continuity test, test-side races removed; matrix T1–T23 in [TEST_STRATEGY.md](TEST_STRATEGY.md) |
| Containers (Phase 13) | `docker/Dockerfile.{api,worker,simulator}` (multi-stage, Alpine, non-root, locked restore), compose profile `app` with `migrate`/`seed` one-off containers and healthchecks; E2E 3/3 against the containers; Trivy 0 HIGH/CRITICAL ([DEPLOYMENT.md §2–§3](DEPLOYMENT.md)) |
| CI / SAST (Phase 14) | `ci.yml` (locked restore, build with analyzers, format, vulnerable-package gate, full test suite, dependency review, gitleaks, image build + size gate + Trivy + E2E against the containers) and `codeql.yml` (CodeQL C#, `security-extended`); badges in the README; no repository secrets ([DEPLOYMENT.md §4](DEPLOYMENT.md)) |

Per-guarantee test evidence (idempotency, concurrency, outbox zero-loss, DLQ, redelivery, chaos, contracts, E2E, …) is listed in the matrix of [TEST_STRATEGY.md](TEST_STRATEGY.md) §3.

## Not implemented yet

Terraform/AWS deployment (ECS Fargate, RDS, SQS, Secrets Manager, CloudWatch alarms, ECR push from CI,
`ForwardedHeaders`/TLS at the load balancer), the Admin/Ops UI (Blazor) and its image, performance and resilience tests
(k6/NBomber), mutation testing (Stryker, optional), documentation hardening and the portfolio release. None of these are
claimed as done anywhere in the documentation.

## Next

- Phase plan, gates and acceptance criteria: [ROADMAP.md](ROADMAP.md).
- Open items by priority: [BACKLOG.md](BACKLOG.md).
- Decisions taken and pending: [DECISIONS.md](DECISIONS.md).
