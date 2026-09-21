# FulfillmentHub

[![CI](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/workflows/ci.yml/badge.svg)](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/workflows/ci.yml)
[![CodeQL](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/workflows/codeql.yml/badge.svg)](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/workflows/codeql.yml)

A backend for **orchestrating orders, payments and deliveries**, written in **C# 14 / .NET 10** as a modular monolith.
It exists to show how real backend problems are handled in code: idempotency, concurrency, consistency between the
database and external effects (transactional outbox), messaging with idempotent consumers, resilient HTTP integrations,
signed webhooks, security and observability — each of them covered by tests that prove the behaviour.

> This is an engineering portfolio project built to demonstrate production-oriented backend practices in C#/.NET.
> It is not a commercial product and does not represent a real logistics or payment operation.

**Status (2026-09-21):** phases 0–14 of 20 complete. An order travels `Created → AwaitingPayment → Paid → DeliveryRequested →
InDelivery → Delivered` end to end, with external effects leaving through the outbox and SQS queues (LocalStack) and
the providers answering through signed webhooks. Alerts fire from real metrics on the local Grafana stack; the whole system
runs in containers (`docker compose --profile deps --profile app up --build`) and the black-box E2E flows pass against
them; CI (build, tests, images, Trivy, E2E) and CodeQL run on GitHub Actions.
Project status in [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md); plan in [docs/ROADMAP.md](docs/ROADMAP.md).

> **Disclaimer.** This project does not connect to Uber infrastructure or to any real payment provider.
> The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.
> No real deliveries, charges, credentials or customers are involved.

## Flow

```
POST /orders ──► stock reservation (xmin) + delivery quote + OrderPlaced in the outbox   [same transaction]
      │
      ▼ the Worker publishes the outbox to SQS (fh-domain-events) and consumes it with deduplication
OrderPlaced ──► payment created at the simulated provider ──► HMAC webhook "paid" ──► Paid (+ OrderPaid)
OrderPaid   ──► delivery created at the simulated "Uber-like" provider ──► status webhooks ──► InDelivery → Delivered
OrderCancelled / late PaymentPaid ──► automatic refund
Periodic reconciliation covers lost webhooks; the DLQ and the admin endpoints cover messages that fail.
```

## Implemented

- **Orders**: `POST /orders` idempotent by `Idempotency-Key` (response replay, 422 on a different payload, 409 while in
  progress), stock reservation with optimistic concurrency (`xmin`) proven by a test with 20 concurrent buyers, delivery
  quote at checkout with an estimated fee as fallback, cancellation with stock return, keyset pagination.
- **Authentication/authorization**: JWT HS256 (`JsonWebTokenHandler`), the built-in `PasswordHasher`, per-role policies,
  deny by default, login rate limiting, indistinguishable login failures, resource authorization (a customer only sees
  their own data).
- **Payments**: simulated provider with its own contract; typed `HttpClient` with `Microsoft.Extensions.Http.Resilience`
  (total timeout → exponential retry with jitter and `Retry-After` → circuit breaker → per-attempt timeout); webhooks with
  constant-time HMAC-SHA256, timestamp tolerance, inbox with deduplication, `GET` verification before applying `paid`;
  reconciliation of pending payments; automatic refund of orders cancelled after capture.
- **Deliveries**: a simulator reproducing a documented subset of the public Uber Direct contract (token, quote, create,
  get, cancel, `event.delivery_status`); token cache with renewal; requote on `expired_quote`; `409 duplicate_delivery`
  reconciled by adopting the existing delivery; out-of-order/duplicate/delayed events recorded without regressing state;
  reconciliation of silent deliveries.
- **Transactional outbox**: domain events written in the same `SaveChanges` as the aggregate; publisher with
  `FOR UPDATE SKIP LOCKED` + lease, exponential backoff, `Failed` after N attempts, admin endpoints to list and requeue;
  no external effect inside an HTTP request (documented exceptions).
- **Messaging (SQS)**: `fh-domain-events` and `fh-webhooks-inbound` queues with dead-letter queues and a redrive policy,
  consumers with long polling, bounded concurrency, visibility backoff, deduplication persisted in the same transaction
  as the effect; LocalStack in the compose; a broker-less (in-process) mode by configuration.
- **Security hardening** (Phase 10): threat model with trust boundaries, dispositions and residual risks
  ([docs/SECURITY.md](docs/SECURITY.md)); security headers, `POST /orders` rate limit per user, 256 KB body limit, a
  guard test that no log message carries PII or secrets, OWASP Top 10 checklist with evidence per item.
- **Observability**: structured logging with `LoggerMessage` (no PII/secrets), OpenTelemetry (ASP.NET Core, HttpClient,
  Npgsql, AWS SDK, runtime), custom spans for providers/outbox/queues with trace propagation through the queue,
  business and operational metrics, correlation id; locally `grafana/otel-lgtm` (Prometheus, Tempo, Loki, Grafana) with
  a provisioned dashboard and **five alert rules** (5xx rate, p95 latency, worker heartbeat, outbox backlog, DLQ) —
  four of them provoked and observed firing end to end ([docs/incidents](docs/incidents/2026-09-21-slow-provider-drill.md)).
- **Testing hardening** (Phase 12): black-box E2E project over the public API (delivered / declined payment / returned
  delivery), provider contract tests (client wire records vs. simulator wire records, both directions), a chaos
  convergence test (simulator failing 30 % of calls → every order still delivered, one payment each), one end-to-end
  trace-id test.
- **Containers** (Phase 13): multi-stage Alpine images for API, Worker and simulator (non-root, 168–209 MB, Trivy 0
  HIGH/CRITICAL), a compose profile that runs the full system with one-off `migrate`/`seed` containers.
- **CI / SAST** (Phase 14): `ci.yml` (locked restore, build with analyzers, format, vulnerable-package gate, full test
  suite with Testcontainers, dependency review, gitleaks, image build + size gate + Trivy + E2E against the containers)
  and `codeql.yml` (CodeQL C#, `security-extended`) — first runs green on 2026-09-21, 1 CodeQL finding triaged as a
  false positive ([docs/DEPLOYMENT.md §4](docs/DEPLOYMENT.md)).
- **Tests**: 248 (137 unit, 6 architecture, 102 integration, 3 black-box E2E) — the integration tests host the API, the Worker and the
  simulator in-process against real PostgreSQL and LocalStack containers (Testcontainers), with webhooks travelling
  between the hosts.

## Stack

C# 14 · .NET 10 · ASP.NET Core Minimal APIs (native validation, ProblemDetails, OpenAPI + Scalar in dev) · EF Core 10 +
Npgsql/PostgreSQL 17 · `Microsoft.Extensions.Http.Resilience` (Polly v8) · AWS SDK for .NET (SQS) + LocalStack ·
OpenTelemetry · xUnit v3 + Shouldly + Testcontainers + NetArchTest · Docker Compose.

No MediatR, AutoMapper, FluentValidation, generic repositories or messaging frameworks: every choice is justified in
[docs/DECISIONS.md](docs/DECISIONS.md) and in the [ADRs](docs/adr/).

## Project structure

```
src/
  FulfillmentHub.Domain            aggregates, value objects, state machines, domain events
  FulfillmentHub.Application       use cases, ports (providers, messaging), outbox handlers, metrics
  FulfillmentHub.Infrastructure    EF Core + migrations, outbox, SQS, provider HTTP clients, JWT, telemetry
  FulfillmentHub.Api               Minimal APIs, auth, idempotency, webhooks, admin
  FulfillmentHub.Worker            outbox publisher, SQS consumers, reconciliations, sweeps
  FulfillmentHub.ProviderSimulator simulated payment and delivery providers (separate host)
tests/
  FulfillmentHub.UnitTests         domain, provider clients against a scripted transport
  FulfillmentHub.IntegrationTests  API + Worker + in-process simulator, PostgreSQL and LocalStack (Testcontainers)
  FulfillmentHub.ArchitectureTests dependencies between layers
docs/                               product, architecture, domain, integrations, security, observability, ADRs
```

## Engineering highlights

| Area | Where to look |
|---|---|
| API idempotency (replay, 422, concurrent 409) | [Api/Idempotency](src/FulfillmentHub.Api/Idempotency), [PlaceOrderTests](tests/FulfillmentHub.IntegrationTests/Orders/PlaceOrderTests.cs) |
| Optimistic concurrency under load | `PlaceOrderHandler`, `PlaceOrder_TwentyBuyersForTheLastUnit_ExactlyOneSucceeds` |
| Retry, backoff, jitter, circuit breaker (opens **and** closes) | [Infrastructure/Providers](src/FulfillmentHub.Infrastructure/Providers), [UnitTests/Payments](tests/FulfillmentHub.UnitTests/Payments), [UnitTests/Deliveries](tests/FulfillmentHub.UnitTests/Deliveries) |
| HMAC webhooks, inbox, out-of-order events, reconciliation | [Api/Webhooks](src/FulfillmentHub.Api/Webhooks), [Infrastructure/Webhooks](src/FulfillmentHub.Infrastructure/Webhooks), [IntegrationTests/Payments](tests/FulfillmentHub.IntegrationTests/Payments), [IntegrationTests/Deliveries](tests/FulfillmentHub.IntegrationTests/Deliveries) |
| Transactional outbox (atomic commit, retry, logical DLQ, requeue) | [Infrastructure/Outbox](src/FulfillmentHub.Infrastructure/Outbox), [OutboxTests](tests/FulfillmentHub.IntegrationTests/Outbox/OutboxTests.cs) |
| SQS: idempotent consumer and dead-letter queue | [Worker/Messaging](src/FulfillmentHub.Worker/Messaging), [SqsMessagingTests](tests/FulfillmentHub.IntegrationTests/Messaging/SqsMessagingTests.cs) |
| Security (JWT, password hashing, deny by default, rate limiting, headers, threat model) | [Api/Identity](src/FulfillmentHub.Api/Identity), [SecurityHeadersMiddleware](src/FulfillmentHub.Api/Middleware/SecurityHeadersMiddleware.cs), [SecurityHeadersTests](tests/FulfillmentHub.IntegrationTests/Api/SecurityHeadersTests.cs), [docs/SECURITY.md](docs/SECURITY.md) |
| Observability, alert rules, executed runbooks | [Infrastructure/Telemetry](src/FulfillmentHub.Infrastructure/Telemetry), [observability/](observability), [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md), [docs/incidents](docs/incidents) |
| CI and SAST | [.github/workflows](.github/workflows), [docs/DEPLOYMENT.md §4](docs/DEPLOYMENT.md) |

## Engineering workflow

I use AI coding agents as part of my development workflow for implementation, investigation, debugging and repetitive
engineering work. AI-generated changes are reviewed, tested and validated before being accepted.

**I don't ship code I can't explain.**

## Roadmap (not implemented yet)

IaC (Terraform) and AWS deployment (ECS Fargate, RDS, SQS, Secrets Manager, CloudWatch alarms, ECR push from CI) →
Admin/Ops UI (Blazor) → performance and resilience tests → documentation hardening → portfolio release. Nothing in this
list is claimed as done anywhere in the documentation.
Details and acceptance criteria per phase in [docs/ROADMAP.md](docs/ROADMAP.md).

## Documentation

Index in [docs/README.md](docs/README.md). Start with [PRODUCT.md](docs/PRODUCT.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md)
and [DOMAIN.md](docs/DOMAIN.md); integrations and the "real vs. simulated" contract in [INTEGRATIONS.md](docs/INTEGRATIONS.md);
decisions in [DECISIONS.md](docs/DECISIONS.md) and [adr/](docs/adr/).

## Running locally

Prerequisites: .NET SDK 10, Docker Desktop. Full walkthrough in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

```bash
cp .env.example .env                        # local Postgres password (not a real secret)
docker compose --profile deps up -d         # PostgreSQL, LocalStack (SQS) and grafana/otel-lgtm (Grafana on :3000)
# or everything in containers (after adding JWT_SIGNING_KEY and SEED_*_PASSWORD to .env):
#   docker compose --profile deps --profile app up --build -d   # then: bash scripts/run-e2e.sh
dotnet tool restore
# development secrets live in user-secrets, never in versioned files:
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<POSTGRES_PASSWORD>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Jwt:SigningKey" "<64 random chars>" --project src/FulfillmentHub.Api
# (remaining keys — seed passwords, dev-only simulator credentials — listed in docs/DEVELOPMENT.md)
dotnet ef database update --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet run --project src/FulfillmentHub.Api -- seed      # fictional development data
dotnet run --project src/FulfillmentHub.ProviderSimulator # http://localhost:5100
dotnet run --project src/FulfillmentHub.Worker
dotnet run --project src/FulfillmentHub.Api               # http://localhost:5000/scalar/v1
```

## Tests

```bash
dotnet test --solution FulfillmentHub.slnx   # 245 tests (Docker required); the 3 E2E tests skip unless FH_E2E_API_URL is set
bash scripts/run-e2e.sh                       # black-box E2E against the running stack (dotnet run hosts or the containers)
```

## Security and observability evidence

| Claim | State | Where to look |
|---|---|---|
| Threat model (assets, actors, trust boundaries, threats, controls, residual risks) | documented | [docs/SECURITY.md §1](docs/SECURITY.md) |
| Structured logs without PII | implemented + guard test | [LogMessagePrivacyTests](tests/FulfillmentHub.ArchitectureTests/LogMessagePrivacyTests.cs) |
| Metrics, traces, correlation id, health checks | implemented | [docs/OBSERVABILITY.md §2–§5](docs/OBSERVABILITY.md), [CorrelationIdMiddleware](src/FulfillmentHub.Api/Middleware/CorrelationIdMiddleware.cs), `/health/live`, `/health/ready` |
| Alert rules evaluated on real metrics | implemented locally (Grafana provisioning) | [observability/grafana/provisioning/alerting](observability/grafana/provisioning/alerting/fulfillmenthub-alerts.yaml), [docs/OBSERVABILITY.md §6](docs/OBSERVABILITY.md) |
| Alerts observed firing and resolving | executed 2026-09-21 (p95, worker heartbeat, DLQ, 5xx) | [docs/incidents/2026-09-21-slow-provider-drill.md](docs/incidents/2026-09-21-slow-provider-drill.md) |
| CI (locked restore, build, analyzers, format, vulnerable packages, tests, images, Trivy, E2E; actions pinned to commit SHAs) | **executed** — [run 35623513605](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35623513605) green: 245 tests, images 144/117/130 MB, Trivy 0 HIGH/CRITICAL, E2E 3/3 against the containers | [.github/workflows/ci.yml](.github/workflows/ci.yml), [docs/DEPLOYMENT.md §4](docs/DEPLOYMENT.md), [docs/SECURITY.md §6.5](docs/SECURITY.md) |
| Containers and black-box E2E | **executed locally and on GitHub** — E2E 3/3 against the compose stack, Trivy 0 HIGH/CRITICAL (2026-09-21) | [docs/DEPLOYMENT.md §2–§3](docs/DEPLOYMENT.md), [tests/FulfillmentHub.E2ETests](tests/FulfillmentHub.E2ETests) |
| SAST with CodeQL | **executed** — [run 35623513653](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35623513653) green, 63 rules, 1 finding triaged (false positive, justified), 0 open | [.github/workflows/codeql.yml](.github/workflows/codeql.yml), [docs/SECURITY.md §6](docs/SECURITY.md) |

## Disclaimer

The payment and delivery providers are **simulated** (`FulfillmentHub.ProviderSimulator`). The delivery one reproduces a
documented subset of the public Uber Direct API contract for educational purposes only; the payment one has its own
contract inspired by the lifecycle common to PSPs. Nothing here connects to real infrastructure, moves money or involves
real customers.

## License

[MIT](LICENSE) © 2026 Gabriel Vitor Pereira Leite
