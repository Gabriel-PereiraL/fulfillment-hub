# BACKLOG — FulfillmentHub

Priorities: **P0** (blocks the phase gate), **P1** (needed for the portfolio, but does not block the current gate),
**P2** (valuable improvement), **P3** (nice-to-have). Rule: **never execute P2/P3 while a P0 of the current phase is open.**
Status: `todo` · `doing` · `done` · `dropped`. Stable IDs (`BL-xxx`) for references in code (`// TODO(BL-042)`), commits and docs.

## Domain

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-001 | Common types: `Entity`, `AggregateRoot`, `IDomainEvent`, `DomainException`, `Money`, `Address`, `EmailAddress`, `PhoneNumber`, typed IDs (Guid v7) | 2 | P0 | done |
| BL-002 | `Product` with `Reserve/Release` and the stock invariant | 2 | P0 | done |
| BL-003 | `Customer` with addresses (owned) | 2 | P0 | done |
| BL-004 | `Order` + `OrderItem` + state machine + `OrderStatusChange` + events | 2 | P0 | done |
| BL-005 | `Payment` + `PaymentAttempt` + state machine | 2 | P0 | done |
| BL-006 | `DeliveryQuote`, `Delivery` + `DeliveryEvent` + canonical ordering rule | 2 | P0 | done |
| BL-007 | `User`, `Role` | 2 | P0 | done |
| BL-008 | Unit tests for every invariant (DOMAIN.md §10) | 2 | P0 | done |
| BL-009 | Decide D-P3 (delivery fee on the order) and adjust `Order`/`Payment` | 4 | P0 | done |
| BL-010 | Human-readable order number via sequence | 2 | P1 | done |

## API

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-020 | Minimal APIs skeleton, ProblemDetails, `IExceptionHandler`, OpenAPI + Scalar | 1 | P0 | done |
| BL-021 | Health checks `/health/live`, `/health/ready` | 1 | P0 | done |
| BL-022 | Correlation id middleware (`X-Correlation-Id`) | 1 | P0 | done |
| BL-023 | `POST /auth/login`, `GET /me` | 3 | P0 | done |
| BL-024 | `POST /orders` with the `Idempotency-Key` filter + `IdempotencyRecord` | 4 | P0 | done |
| BL-025 | `GET /orders/{id}`, `GET /orders` (keyset pagination), resource authorization (a customer only sees their own) | 4 | P0 | done |
| BL-026 | `POST /orders/{id}/cancel` | 4 | P0 | done |
| BL-027 | `POST /webhooks/payments` (signature, dedup, fast 200) | 5 | P0 | done |
| BL-028 | `POST /webhooks/deliveries` | 7 | P0 | done |
| BL-029 | Catalog endpoints: `GET /products` (**done**, Phase 4); admin product CRUD pending | 4/17 | P1 | doing |
| BL-030 | Operations endpoints: requeue outbox/webhook, reconcile, cancel delivery | 8–9 | P1 | partial (outbox: list `Failed` + retry ✔ Phase 8; webhooks/reconcile/cancel → Phase 17) |
| BL-031 | Native .NET 10 validation (`AddValidation`) on requests | 4 | P0 | done |
| BL-032 | API versioning (`/v1`) — decide whether via a fixed path | 4 | P2 | todo |
| BL-033 | Refresh tokens | 10 | P2 | todo |
| BL-034 | Example `.http` files per module | 4+ | P2 | todo |

## Database

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-040 | `FulfillmentHubDbContext` + `IFulfillmentHubDbContext` + initial migration + working `dotnet ef` | 1 | P0 | done |
| BL-041 | EF configurations per entity; owned/complex types; ID converters; `xmin` as concurrency token | 2 | P0 | done |
| BL-042 | Constraints: `CHECK stock >= 0`, partial `UNIQUE` (active payment/delivery per order), `UNIQUE(provider, provider_event_id)` | 2 | P0 | done |
| BL-043 | Indexes for list queries (customer_id+created_at, status), outbox (`status,next_attempt_at`), webhook | 2 | P0 | done |
| BL-044 | Development seed via an explicit command (`dotnet run -- seed`) — moved from Phase 2: depends on the `PasswordHasher` | 3 | P1 | done |
| BL-045 | Audit interceptor (`CreatedAt/UpdatedAt`) and `audit_logs` for operational actions | 2/8 | P1 | todo |
| BL-046 | Strategy for applying migrations in the cloud (one-off task / `migrations script --idempotent`) | 16 | P0 | todo |
| BL-047 | `ExecutionStrategy` (retry of Npgsql transient errors) configured and tested with explicit transactions | 8 | P1 | todo |

## Integrations

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-060 | `/payments/v1` payment simulator (INTEGRATIONS §3 contract, scenarios, signed webhooks) | 5 | P0 | done |
| BL-061 | `IPaymentGatewayClient` typed client + resilience + idempotency | 5 | P0 | done |
| BL-062 | `/delivery/v1` delivery simulator (token, quote, create, get, cancel, errors, lifecycle, webhooks) | 6 | P0 | done |
| BL-063 | `IDeliveryProviderClient` + pipeline (timeout/retry/CB) + tested retry matrix | 6 | P0 | done |
| BL-064 | Requote on `expired_quote`; reconciliation on `409 duplicate_delivery` | 6 | P0 | done |
| BL-065 | Simulator scenarios: latency, failure rate, timeout, 429+Retry-After, 503 couriers_busy, duplicate/delayed/out-of-order webhook, `SIM_FORCE_STATUS` | 6–7 | P0 | done (out-of-order/duplicate/delayed emitted and consumed in Phase 7; `SIM_FORCE_STATUS` → BL-066) |
| BL-247 | Charge the difference (or refund) when the fee quoted at checkout differs from the real delivery cost — today the store absorbs it (D-51) | 17+ | P3 | todo |
| BL-248 | Sweep of orphan `Requested` deliveries without a `Paid` order (e.g. order cancelled between local creation and confirmation) — today logs only | 8 | P2 | todo |
| BL-066 | Simulator admin route (`POST /admin/scenario`) | 7 | P1 | todo |
| BL-067 | Periodic reconciliation of payments and deliveries (`GET` at the provider) | 5/8 | P1 | done (payments Phase 5, deliveries Phase 7: `DeliveryReconciliationService`) |
| BL-244 | Automatic refund when the provider captures after the customer cancelled (`paid_after_cancellation`) — previously log 5002 + metric only | 8 | P1 | done (`RefundPaymentHandler` via `OrderCancelled`/`PaymentPaid`) |
| BL-245 | Simulator: `WebhookFailFirstN` (forces webhook delivery retry) and an out-of-order webhook scenario | 7 | P2 | todo |
| BL-068 | Contract tests: simulator schemas vs. client DTOs | 12 | P1 | todo |
| BL-069 | Second delivery provider (`AlternativeProvider`) to demonstrate substitution | 18+ | P3 | todo |
| BL-070 | Cache/renewal of the simulator's OAuth-like token (401 → renew once) | 6 | P1 | done (`DeliveryAccessTokenProvider` + `DeliveryBearerTokenHandler`, D-54) |

## Messaging

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-080 | `OutboxMessage` + `SaveChanges` interceptor (events → outbox in the same commit) | 8 | P0 | done |
| BL-081 | `OutboxPublisher` (`SKIP LOCKED`, batch, backoff+jitter, `Failed` after N) | 8 | P0 | done |
| BL-082 | In-process handlers: `OrderPlaced`→payment, `OrderPaid`→delivery, `OrderCancelled`→stock/refund | 8 | P0 | done |
| BL-083 | Test "crash between commit and publish loses no event" | 8 | P0 | done (T12 in `OutboxTests`) |
| BL-084 | `IMessagePublisher` SQS + queues/DLQ via LocalStack (compose init + Testcontainers) | 9 | P0 | done |
| BL-085 | SQS consumers: long polling, visibility, bounded concurrency, `processed_messages` (idempotent consumer) | 9 | P0 | done |
| BL-086 | Webhooks → `fh-webhooks-inbound` | 9 | P0 | done (in-process fallback when the broker is disabled/unavailable) |
| BL-087 | Metrics: outbox lag, DLQ count, message age, retries | 9/11 | P0 | done (Phase 9: `fh.queue.*`, `fh.outbox.*`) |
| BL-088 | Trace context propagation (outbox → SQS attributes → consumer) | 11 | P0 | done (Phase 9: `traceparent` attribute → the `receive` span continues the trace) |
| BL-089 | Redrive the DLQ from the Admin | 17 | P1 | todo |

## Security

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-100 | user-secrets + `.env.example`; no secret in `appsettings` | 1 | P0 | done |
| BL-101 | JWT (HS256, key ≥ 32 bytes from secrets, short expiry), authenticated `FallbackPolicy`, per-role policies | 3 | P0 | done |
| BL-102 | `PasswordHasher<T>` (PBKDF2) and a minimal password policy | 3 | P0 | done |
| BL-103 | Rate limiting: login (**done**, Phase 3), webhooks (**done**, Phase 5, configurable D-69) and `POST /orders` per user (D-84) | 3/10 | P0 | done (2026-09-21, hardening track) |
| BL-104 | HMAC webhook signature (constant time) + timestamp tolerance + body limit | 5/7 | P0 | done (payment; delivery reuses it in Phase 7) |
| BL-105 | Resource authorization (a customer only accesses their own orders) + broken access control tests | 4/10 | P0 | done |
| BL-106 | Security headers + HSTS outside Development (D-82); CORS reviewed and **deliberately not enabled** (D-82); Kestrel body limit (D-83) | 10 | P0 | done (2026-09-21, hardening track) |
| BL-113 | HTTPS redirection / `ForwardedHeaders` behind the ALB (TLS terminates at the edge) | 16 | P0 | todo |
| BL-107 | Threat model reviewed with evidence (assets, actors, trust boundaries, surfaces, threats, controls, residual risks, limitations); planned items dispositioned (D-87); OWASP Top 10 checklist filled in | 10 | P0 | done (2026-09-21, hardening track) |
| BL-108 | PII in logs: catalog audited (no e-mail/phone/token/body placeholders) + guard test over every `LoggerMessage` (D-85) | 10 | P0 | done (2026-09-21, hardening track) |
| BL-109 | `dotnet list package --vulnerable` as a CI gate; CodeQL (SAST) + dependency review + gitleaks in CI (D-81) — Trivy moves to BL-164 (images, Phase 13/14) | 10/14 | P0 | done (2026-09-21; Trivy → BL-164) |
| BL-110 | Secrets Manager on AWS (the task role reads secrets; nothing in plain env) | 15–16 | P0 | todo |
| BL-111 | SSRF: no user-supplied URL is ever called (provider base URLs are validated configuration) — documented in the threat model | 10 | P1 | done (2026-09-21, hardening track) |

## Observability

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-120 | Structured JSON logging + `LoggerMessage` source generator + scopes with the correlation id | 1 | P0 | done |
| BL-121 | OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, runtime) → OTLP → local backend (Aspire Dashboard in Phase 1; `grafana/otel-lgtm` from the hardening track, D-78) | 1 | P0 | done |
| BL-122 | Project `ActivitySource`/`Meter`; spans for use cases, provider calls, outbox, consumers | 5–11 | P0 | partial (`FulfillmentHub` meter ✔; `Provider *` spans ✔ Phases 5/6; outbox ✔ Phase 8; queue consumers ✔ Phase 9; use-case spans → Phase 11) |
| BL-246 | Dedicated provider retry/circuit metrics (`fh.provider.retry.count`, `fh.provider.circuit.state`) — today only the standard `HttpClient` instrumentation | 11 | P2 | todo |
| BL-123 | Business/operations metrics (OBSERVABILITY.md table) | 11 | P0 | todo |
| BL-124 | AWS SDK (SQS) instrumentation | 9 | P1 | done (`OpenTelemetry.Instrumentation.AWS`) |
| BL-125 | Incident runbook executed locally with evidence (`docs/incidents/`) | 11 | P0 | done (2026-09-21, hardening track) |
| BL-126 | CloudWatch alarms re-expressing the local rules (BL-129) + DLQ > 0 | 16 | P0 | todo |
| BL-128 | Local metrics/alerting backend `grafana/otel-lgtm` in compose (D-78), provisioned datasources + one dashboard, Aspire Dashboard as opt-in profile | 11 | P0 | done (2026-09-21, hardening track) |
| BL-129 | Five provisioned alert rules (D-79): `ApiHigh5xxRate`, `ApiHighLatencyP95`, `WorkerHeartbeatMissing`, `OutboxBacklog`, `QueueDlqNotEmpty`; catalog with signal/threshold/window/reason/impact/cause/action | 11 | P0 | done (2026-09-21, hardening track) |
| BL-130 | Reproducible fault scenarios with existing knobs (D-80): slow/failing provider, database outage, stopped worker; `scripts/` helpers + docs | 11 | P0 | done (2026-09-21, hardening track) |
| BL-127 | Test: one order = one end-to-end `trace_id` | 11 | P1 | todo |

## Tests

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-140 | Test projects + xUnit v3 + Shouldly + Testcontainers + `WebApplicationFactory` base | 1 | P0 | done |
| BL-141 | ArchitectureTests (NetArchTest): dependencies, conventions (`sealed`, suffixes) | 1 | P0 | done |
| BL-142 | `POST /orders` idempotency (3 scenarios) | 4 | P0 | done |
| BL-143 | Stock race condition (fails without the token, passes with it) | 4 | P0 | done |
| BL-144 | Duplicate / delayed / out-of-order webhook (payment and delivery) | 5/7 | P0 | done (payment T6 Phase 5; delivery T6/T7 Phase 7) |
| BL-145 | Retry matrix with a fake `HttpMessageHandler`; circuit breaker opens/closes | 5/6 | P0 | done (retry ✔ T9, opens ✔ T10 Phase 5; half-open/closes ✔ T10 Phase 6) |
| BL-146 | Outbox: zero loss, retry, `Failed` | 8 | P0 | done |
| BL-147 | SQS: DLQ after N, idempotent consumer | 9 | P0 | done (T14/T15 in `SqsMessagingTests`) |
| BL-148 | E2E with compose: 3 flows | 12 | P0 | todo |
| BL-149 | Authorization tests: 401/403 per role (Phase 3) and cross-resource access (Phase 4) | 3/4 | P0 | done |
| BL-150 | Convergence with `SIM_FAILURE_RATE=0.3` | 12 | P1 | todo |
| BL-151 | Stryker (mutation) on Domain | 12 | P3 | todo |
| BL-152 | Integration suite is intermittent: on 2026-09-18 a full run had 4 failures on a cold Docker start (`PaymentFlowTests` among them) and another had 2 (`OrderAccessAndCancelTests`: a test helper writing the order while the in-process outbox publisher updates it → `DbUpdateConcurrencyException`/500); each time the following run was 225/225. Investigate the test-side races (pause the publisher in those helpers, container start timing) instead of retrying | 12 | P1 | todo |

## DevOps

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-160 | `docker-compose.yml` (Postgres 17, Aspire Dashboard; LocalStack in Phase 9; simulator in Phase 6) | 1 | P0 | done |
| BL-161 | `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` | 1 | P0 | done |
| BL-162 | Local `git init`, clean first commit (no secrets or local files) | 1 | P0 | done |
| BL-163 | Multi-stage non-root Dockerfiles + healthcheck (Api, Worker, Simulator, Admin) | 13 | P0 | todo |
| BL-164 | GitHub Actions: image build + Trivy + push to ECR (OIDC) — after Phase 13 | 14 | P0 | todo |
| BL-166 | `ci.yml`: restore, build Release, format check, vulnerable-package gate, unit/architecture/integration tests (Testcontainers), test results artifact (D-81) | 14 | P0 | done (2026-09-21, workflow committed + linted; first GitHub run pending push) |
| BL-167 | `codeql.yml`: CodeQL C# (SAST) on push/PR/schedule, results in the Security tab; findings triage procedure and limitations documented | 14 | P0 | done (2026-09-21, workflow committed + linted; first GitHub run pending push) |
| BL-168 | Dependency review (PRs) + gitleaks jobs | 14 | P1 | done (2026-09-21; first GitHub run pending push) |
| BL-169 | NuGet lock file (`RestorePackagesWithLockFile` + `--locked-mode` in CI) | 14 | P2 | todo |
| BL-165 | `scripts/` (migrate, seed, run-e2e) | 12 | P1 | todo |

## AWS

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-180 | Terraform: VPC, subnets, SG, endpoints/NAT (cost decision) | 15 | P0 | todo |
| BL-181 | Terraform: ECR, ECS cluster, task definitions (Api, Worker, Simulator), Fargate services, ALB | 15 | P0 | todo |
| BL-182 | Terraform: RDS PostgreSQL (t4g.micro), restricted SG, parameter group | 15 | P0 | todo |
| BL-183 | Terraform: SQS + DLQ, least-privilege IAM task roles, Secrets Manager, CloudWatch log groups/alarms, budget | 15 | P0 | todo |
| BL-184 | Dev deploy + one-off migrations + E2E verification in the cloud + destroy | 16 | P0 | todo |
| BL-185 | ADOT collector sidecar → CloudWatch/X-Ray | 16 | P0 | todo |
| BL-186 | Document the real observed costs | 16 | P1 | todo |

## Documentation

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-200 | Phase 0: all base docs + ADRs | 0 | P0 | done |
| BL-201 | Update DOMAIN.md/INTEGRATIONS.md as implemented (every phase) | all | P0 | todo |
| BL-202 | Simulator README with the disclaimer | 6 | P0 | done |
| BL-203 | Decide the final documentation language (pt-BR vs. en) | 19 | P1 | done (English; public docs rewritten on 2026-09-18) |
| BL-204 | Diagrams (C4 levels 1–2, sequence) | 19 | P1 | todo |
| BL-205 | Reading guide for recruiters ("start here") | 19 | P1 | todo |

## UX/Admin

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-220 | Blazor Web App (server interactive) with cookie/JWT authentication and policies | 17 | P0 | todo |
| BL-221 | Lists: orders, payments, deliveries, webhooks, outbox, DLQ, integrations (CB state, latency) | 17 | P0 | todo |
| BL-222 | Actions: requeue, cancel delivery, reconcile, redrive DLQ | 17 | P0 | todo |
| BL-223 | Search by correlation id → order timeline | 17 | P1 | todo |

## Performance

| ID | Item | Phase | Pri | Status |
|---|---|---|---|---|
| BL-240 | Load tool (k6 vs NBomber) — decide | 18 | P0 | todo |
| BL-241 | Scenarios: concurrent `POST /orders`, webhook burst, slow simulator; measure p95/errors/lag | 18 | P0 | todo |
| BL-242 | Indexes/keyset/compiled queries where measured | 18 | P1 | todo |
| BL-243 | Bulkhead per provider if needed (measure first) | 18 | P2 | todo |
