# ROADMAP — FulfillmentHub

Incremental roadmap in phases with a **gate** at the end of each one. No phase advances without its acceptance criteria.
Status: `todo` · `in-progress` · `done` · `blocked`. The order was adjusted from the original proposal: **Auth came before
Orders** (an order needs a principal; retrofitting authentication later causes rework), **Docker Compose for dependencies
moved into Phase 1** (a local database is needed from the start) and **basic observability** (structured logging,
correlation id, OTel skeleton) also moved into Phase 1 (invariant: it is not an afterthought). The CI and AWS phases require
the user's **explicit authorization** for external actions (GitHub, AWS account, costs).

| # | Phase | Status | Gate |
|---|---|---|---|
| 0 | Documentation and architecture | **done (2026-09-18)** | Gate 0 |
| 1 | .NET solution + foundation | **done (2026-09-18)** | Gate 1 |
| 2 | Domain and database | **done (2026-09-18)** | Gate 2 |
| 3 | Identity + Auth (JWT, roles) | **done (2026-09-18)** | Gate 3 |
| 4 | Orders (API + idempotency + stock concurrency) | **done (2026-09-18)** | Gate 4 |
| 5 | Payments (simulator + integration + webhook + reconciliation) | **done (2026-09-18)** | Gate 5 |
| 6 | Delivery provider simulator (Uber-like) + resilient outbound integration | **done (2026-09-18)** | Gate 6 |
| 7 | Delivery webhooks + idempotency + out-of-order events | **done (2026-09-18)** | Gate 7 |
| 8 | Transactional outbox + Worker | **done (2026-09-18)** | Gate 8 |
| 9 | SQS (LocalStack) — producer/consumer, DLQ, idempotent consumer | **done (2026-09-18)** | Gate 9 |
| 10 | Security hardening (threat model, OWASP, rate limiting, headers, secrets) | todo | Gate 10 |
| 11 | Observability hardening (metrics, traces, local dashboards, runbook) | todo | Gate 11 |
| 12 | Testing hardening (E2E, contract tests, chaos via simulator) | todo | Gate 12 |
| 13 | Docker images + full compose | todo | Gate 13 |
| 14 | CI (GitHub Actions) — the public repository already exists; no workflow yet | todo | Gate 14 |
| 15 | AWS IaC (Terraform) — **requires an AWS account/cost authorization** | todo | Gate 15 |
| 16 | Cloud deployment (ECS Fargate, RDS, SQS, Secrets, CloudWatch alerts) | todo | Gate 16 |
| 17 | Admin/Ops UI (Blazor) | todo | Gate 17 |
| 18 | Performance & resilience tests | todo | Gate 18 |
| 19 | Documentation hardening | todo | Gate 19 |
| 20 | Portfolio release (anti-leak review, final README) | todo | Gate 20 |

---

## Phase 0 — Documentation and architecture — `done`
**Goal**: enough documentation for "continue the project" to work without extra context.
**Tasks**: inspect the environment; choose .NET; research Uber Direct and skills; name; architecture; domain; docs; roadmap; backlog; ADRs; PROJECT_STATE; .gitignore; CLAUDE.md; private skills; skills index.
**Acceptance criteria (Gate 0)**: every file in `docs/` + `docs/adr/` exists and is coherent; CLAUDE.md points to PROJECT_STATE and SKILLS_INDEX; `.gitignore` covers private files/secrets; the manual "new session" test is described in `.ai/CHECKLIST.md`.
**Dependencies**: none. **Risks**: over-documenting without code (mitigated: Phase 1 starts in the next session).

## Phase 1 — .NET solution + foundation — `done`
**Goal**: an executable skeleton compiling with analyzers, local database, migrations, health check, first tests, minimal telemetry.
**Tasks**:
1. `dotnet new sln`; projects `Domain`, `Application`, `Infrastructure`, `Api`, `Worker`, `ProviderSimulator`; tests `UnitTests`, `IntegrationTests`, `ArchitectureTests`.
2. `Directory.Build.props` (Nullable, TreatWarningsAsErrors, AnalysisLevel latest-recommended, LangVersion), `Directory.Packages.props` (CPM), `.editorconfig`, `global.json` (SDK 10.0.x).
3. `docker-compose.yml`: PostgreSQL 17, Aspire Dashboard (OTLP), LocalStack (only from Phase 9 — leave commented out).
4. Empty `FulfillmentHubDbContext` + initial migration + working `dotnet ef`; `IFulfillmentHubDbContext`.
5. Typed options (`ConnectionStrings`, `Telemetry`), user-secrets configured, `appsettings.Development.json` without secrets.
6. Health checks: `/health/live`, `/health/ready` (Npgsql).
7. ProblemDetails + `IExceptionHandler`; OpenAPI + Scalar in Development.
8. Structured logging (JSON console), correlation id middleware (`X-Correlation-Id`), OpenTelemetry (ASP.NET Core, HttpClient, Npgsql) → OTLP.
9. ArchitectureTests: dependency direction; UnitTests: 1 `Money` test; IntegrationTests: `WebApplicationFactory` + Testcontainers PostgreSQL running `/health/ready`.
10. Local `git init` (no remote); first commit after a `git status` free of private files.
**Acceptance criteria (Gate 1)**: `dotnet build` without warnings; `dotnet test` green (unit + arch + integration with a container); `docker compose up` brings up Postgres and the Aspire Dashboard; the API starts, `/health/ready` OK, trace visible in the dashboard; `git status` shows no private files.
**Dependencies**: Docker Desktop running. **Risks**: .NET 10 package versions (validate on NuGet during the session); Testcontainers on Windows (Docker Desktop with WSL2).
**Result (2026-09-18)**: all criteria met; 28 green tests; visual confirmation of the trace in the Aspire Dashboard left to the user. Adjustments from the plan: tests run on the Microsoft.Testing.Platform (xunit.v3 + .NET 10 SDK); database health check via Microsoft's EF Core package; empty initial migration (the model arrives in Phase 2).

## Phase 2 — Domain and database — `done`
**Goal**: the domain model of the Catalog, Customers, Orders, Payments, Deliveries and Identity modules with tested invariants and a consistent schema.
**Tasks**: Common types (Entity, AggregateRoot, Money, Address, IDs); entities and state machines; EF configurations (owned/complex types, converters, `xmin`, constraints, indexes, order number sequence); migration; development seed (products, admin user) via an explicit command; unit tests of the invariants (DOMAIN.md §10).
**Gate 2**: 100% of the listed invariants have a test; the migration applies to a clean database; the `CHECK`/`UNIQUE` constraints exist (integration test that tries to violate them); ArchitectureTests green.
**Risks**: over-modelling — keep only what the flows use.
**Result (2026-09-18)**: 6 modules implemented (Catalog, Customers, Orders, Payments, Deliveries, Identity), `DomainModel` migration, 83 unit + 8 integration tests (round-trips, CHECK, partial unique, `xmin` conflict); `HasPendingModelChanges()` false. Deviations recorded in DOMAIN.md §12 and D-27…D-30. Development seed (BL-044) moved to Phase 3 (needs the password hash).

## Phase 3 — Identity + Auth — `done`
**Goal**: users, roles, JWT login, policies; the API denies by default.
**Tasks**: `User`/`Role`; `PasswordHasher`; `POST /auth/login` (rate-limited); JWT issuance (HS256, key from secrets, short expiry); `FallbackPolicy`; `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly` policies; `GET /me`; admin seed via command; tests (login ok/failure, 401/403, expired token).
**Gate 3**: endpoints protected by default; per-role authorization tests; JWT secret outside the code; SECURITY.md updated with the design.
**Risks**: temptation to use full ASP.NET Identity — no; only `PasswordHasher<T>`.
**Result (2026-09-18)**: JWT HS256 via `JsonWebTokenHandler`/`AddJwtBearer`, `PasswordHasher<User>`, `FallbackPolicy` + 3 policies, login with rate limiting (5/min/IP), `GET /me`, `GET /users/{id}` (AdminOnly), development seed, native .NET 10 validation; 24 new tests (143 in total). Evidence in SECURITY.md §2. A non-existent route now answers 401 to anonymous callers (D-33).

## Phase 4 — Orders — `done`
**Goal**: create/query/cancel orders with real idempotency and demonstrated stock concurrency.
**Tasks**: `POST /orders` (Idempotency-Key filter + `IdempotencyRecord`), `GET /orders/{id}`, `GET /orders` (keyset pagination), `POST /orders/{id}/cancel`; stock reservation with `xmin`; status history; decision D-P3 (delivery fee on the order); tests: idempotency (same key → same response; different hash → 422; concurrent → 409), stock race (N parallel tasks → never negative), transitions.
**Gate 4**: the race-condition scenario has a test that fails without the concurrency token and passes with it; idempotency covered; OpenAPI with every endpoint; the Orders P0 BACKLOG closed.
**Risks**: idempotency with large responses — store the body (jsonb) with a TTL.
**Result (2026-09-18)**: `POST/GET/LIST /api/v1/orders`, `POST /orders/{id}/cancel`, `GET /api/v1/products`; `Idempotency-Key` filter + `idempotency_records` (ADR-010 implementation); stock reservation with `xmin` + limited retry; resource authorization; keyset by order number; 17 new integration tests (160 in total). T4 verified failing without the token (3/3). D-P3 partially resolved (D-36: null fee until Phase 6). Findings: client-side keys need `ValueGeneratedNever` (D-39); included collections are not ordered (D-40).

## Phase 5 — Payments — `done`
**Goal**: simulated payment provider + integration + idempotent webhook + reconciliation + transient/permanent failures.
**Tasks**: `/payments/v1` simulator (INTEGRATIONS §3 contract, scenarios); `IPaymentGatewayClient` typed client + resilience; `CreatePaymentForOrder` (in-process, triggered right after `PlaceOrder` in this phase; via the outbox in Phase 8); `POST /webhooks/payments` (signature, dedup, processing); `ReconcilePayments` job (minimal Worker); `Payment`/`Order` transitions; tests: duplicate webhook, delay, transient failure (retry) vs permanent (cancels the order and releases stock).
**Gate 5**: the order→paid flow passes in integration with the in-process simulator; a duplicate webhook has no double effect; reconciliation corrects a pending payment.
**Risks**: simulator complexity — keep it in memory and small.
**Result (2026-09-18)**: `/payments/v1` simulator (bearer, `Idempotency-Key` with replay/409, automatic settlement, HMAC webhooks with retry, `Simulator:Chaos`, sandbox amounts `…99`/`…98`); `IPaymentGatewayClient` + `Microsoft.Extensions.Http.Resilience` (timeout → retry → CB → per-attempt timeout); `CreatePaymentForOrderHandler` in-process after `PlaceOrder` (D-44); `webhook_events` + `POST /api/v1/webhooks/payments` (constant-time HMAC, timestamp, dedup, 64 KB, rate limit) with D-P5 = yes; `PaymentReconciliationService` in the Worker (recreates at the provider when there is no id; re-reads pending ones). Tests: 185 (10 resilience unit tests, 14 payment integration tests). Gate 5 closed; Kestrel smoke test with API + simulator + worker. Findings: unreadable JSON produced a 500 in Development (D-49); a late capture after cancellation needed its own rule (D-47).

## Phase 6 — Delivery provider simulator + outbound integration — `done`
**Goal**: an "Uber-like" simulator (documented subset) and a resilient quote/create/get/cancel client.
**Tasks**: `/delivery/v1` routes (token, quotes, deliveries, get, cancel) with the errors/scenarios of INTEGRATIONS §2; timed lifecycle; `IDeliveryProviderClient` + resilience pipeline + retry matrix; `DeliveryQuote`/`Delivery` creation; requote on `expired_quote`; `409 duplicate_delivery` reconciled; tests with a fake `HttpMessageHandler` (429 + Retry-After, 503, timeout, 400 without retry) and integration with the in-process simulator.
**Gate 6**: retry matrix covered by tests; the circuit breaker opens/closes in a test; disclaimer present in the simulator README.
**Result (2026-09-18)**: `/delivery/v1` simulator (short-lived fake client-credentials token, quote/create/get/cancel with the reference contract's field names, `expired_quote`/`used_quote`/`duplicate_delivery`+metadata/`noncancelable_delivery`/`couriers_busy`/`customer_limited` errors, timed lifecycle with `event.delivery_status` webhooks signed in `X-Uber-Signature` — only emitted from Phase 7; sandbox rules by postal code); `IDeliveryProviderClient` with the shared pipeline (D-53) + token handler (D-54); D-P3 resolved (D-51: quote at checkout, estimated fee as fallback); `RequestDeliveryHandler` in the Worker (D-52) with a single requote (D-55), adoption on `409` (D-56), cancellation at the provider (D-57). Tests: 211 (11 delivery client unit tests incl. CB opens **and closes**; 10 delivery integration tests). Gate 6 closed; Kestrel smoke test (order → R$17 quote → paid → delivery requested by the worker in ~7 s). Finding: per-process randomized string hashing made the simulator fee non-deterministic (D-58).

## Phase 7 — Delivery webhooks + idempotency + out-of-order — `done`
**Goal**: ingestion of `event.delivery_status` with signature, dedup and the ordering rule.
**Tasks**: `POST /webhooks/deliveries`; `WebhookEvent`; `ApplyDeliveryWebhookHandler` with the canonical order; `Order` follows (`InDelivery`, `Delivered`, `Cancelled`); the simulator sending duplicated/delayed/out-of-order webhooks; tests: `delivered` before `pickup` does not regress; duplicate → no-op; invalid signature → 401.
**Gate 7**: the "out-of-order webhook" and "duplicate" scenarios with green integration tests; manual E2E: an order reaches `Delivered` with the simulator in chaotic mode.
**Result (2026-09-18)**: `POST /api/v1/webhooks/deliveries` on the shared `WebhookReceiver` pipeline (D-60; `X-Uber-Signature` header, its own key); `ApplyDeliveryWebhookHandler` + `DeliveryStatusApplier` with aggregate dispositions and the order following (D-61); `ReconcileDeliveriesHandler` + `DeliveryReconciliationService` (D-62, `PeriodicJob` base); the simulator emitting webhooks in Development and in the tests, silent postal code `…003` (D-63); D-59 (no `GET` before applying a delivery). Tests: 217 (6 new: end to end order → delivered through real webhooks, `returned` cancels + stock, T7 out-of-order + duplicate, invalid signature/header/timestamp → 401, unknown delivery → `Ignored`, lost webhooks recovered by reconciliation). Gate 7 closed; Kestrel smoke test: `AwaitingPayment → Paid → DeliveryRequested → InDelivery → Delivered` in 18 s with 4 webhooks applied.

## Phase 8 — Transactional outbox + Worker — `done`
**Goal**: eliminate the dual write: domain events persisted in the commit and processed by the Worker with retry/logical DLQ.
**Tasks**: `OutboxMessage`, interceptor, `OutboxPublisher` (`SKIP LOCKED`), in-process dispatch to handlers (`OrderPlaced`→payment, `OrderPaid`→delivery, `OrderCancelled`→stock/refund), backoff+jitter, `Failed` after N, admin requeue endpoint, lag metrics; migrate the in-process triggers of Phases 5–6 to the outbox; tests: a failure injected between commit and publish loses no event; a failing handler is re-executed; after N it goes to `Failed`.
**Gate 8**: no external effect outside the worker; the "crash after commit" test passes; ADR-004 updated with what was implemented.
**Result (2026-09-18)**: `outbox_messages` + `OutboxInterceptor` (events written in the same `SaveChanges`), `OutboxProcessor` (`FOR UPDATE SKIP LOCKED` + lease, one scope per message, exponential backoff with jitter, `Failed` after N, span linked to the request trace), `OutboxPublisherService` in the Worker (500 ms), handlers `OrderPlaced` → payment, `OrderPaid` → delivery, `OrderCancelled`/`PaymentPaid` → refund (BL-244 closed), admin `GET/POST /api/v1/admin/outbox` (D-64…D-68); `POST /orders` answers `Created`; configurable webhook rate limit (D-69, found by the suite). Tests: 222 (6 `OutboxTests`: T12 ×2, T13, admin for Admin only, idempotent redelivery, refund on cancellation; the fixture hosts a pausable publisher). Gate 8 closed; ADR-004 with an "Implementation" section; Kestrel smoke test `Created → … → Delivered` in 18 s through the outbox alone.

## Phase 9 — SQS — `done`
**Goal**: the outbox publishes to SQS; consumers in the Worker; DLQ; idempotent consumer; local LocalStack; Testcontainers LocalStack.
**Tasks**: `IMessagePublisher` (AWS SDK), queues + DLQ created by script/compose init; consumers with long polling, visibility, bounded concurrency; `processed_messages`; webhooks now go to `fh-webhooks-inbound`; metrics (approximate depth, message age, DLQ count); integration tests with LocalStack; documentation of the trade-off (INTEGRATIONS §6).
**Gate 9**: a poison message goes to the DLQ after `maxReceiveCount`; a consumer receives a duplicate and does not duplicate the effect; the full compose works.
**Result (2026-09-18)**: `Messaging:Sqs` (D-70 mode key), `IAmazonSQS` + `SqsQueueProvisioner` (queues + DLQ with redrive, D-71), `IMessagePublisher`/`SqsMessagePublisher` (JSON envelope + `type`/`traceparent`), `OutboxProcessor` publishing to `fh-domain-events`, consumers in the Worker (`SqsConsumer` base with long polling, bounded concurrency, delete after success, visibility backoff D-75; `DomainEventsConsumer` with `processed_messages` dedup in the same transaction D-72; `WebhooksInboundConsumer`), webhooks through `fh-webhooks-inbound` with a pointer + in-process fallback (D-73/D-74), `IWebhookProcessor` per provider, `fh.queue.*` metrics, OTel instrumentation of the SDK, LocalStack in the compose. Tests: 225 (3 `SqsMessagingTests` with Testcontainers LocalStack: flow through the queues, T14 redelivery without a second effect, T15 poison message → DLQ after 3). Gate 9 closed; ADR-005 with "Implementation"; smoke test with the full compose: `Created → … → Delivered` in 16 s with webhooks applied by the Worker.

## Phase 10 — Security hardening
**Goal**: SECURITY.md "implemented", not "planned".
**Tasks**: threat model reviewed; rate limiting (login, webhooks, creation); security headers; explicit CORS; validation/size limits; mass assignment reviewed; logs without PII (redaction); `dotnet list package --vulnerable` in the local build; secrets audited; resource authorization (a customer only sees their own orders) with tests; OWASP Top 10 checklist filled in with evidence.
**Gate 10**: checklist with a link to code/test for every item; broken access control tests.

## Phase 11 — Observability hardening
**Goal**: metrics/traces/logs that are useful for operating; incident runbook.
**Tasks**: our own metrics (OBSERVABILITY.md); spans in worker/outbox/consumers with context propagation (trace parent stored in the outbox and in the SQS message); local dashboards (Aspire Dashboard + saved queries); planned alerts (thresholds); "customer reports slowness" runbook; a test verifying that one order produces a single end-to-end `trace_id`.
**Gate 11**: runbook actually executed locally with a slow simulator and evidence (screenshots/record in docs).

## Phase 12 — Testing hardening
**Goal**: E2E of the main flows, simulator contract tests, chaos.
**Tasks**: E2E with compose (order→delivery, order→failed payment, order→cancelled delivery); contract tests (simulator schemas vs. client DTOs); a run with `SIM_FAILURE_RATE=0.3` asserting convergence; optional mutation coverage (Stryker) on Domain; clean-up of weak tests.
**Gate 12**: E2E green; TEST_STRATEGY.md with the "scenario → test" matrix.

## Phase 13 — Docker images + full compose
**Goal**: multi-stage Dockerfiles (Api, Worker, Simulator, Admin), non-root user, healthcheck; compose brings everything up.
**Gate 13**: `docker compose up --build` → the E2E flow passes against containers; images < 250 MB; local scan (Trivy/`docker scout`) without criticals.

## Phase 14 — CI (GitHub Actions) — requires authorization
**Goal**: restore/build/analyzers/unit/integration (Testcontainers)/security scan (CodeQL, dependency review, gitleaks, Trivy)/image build/artifact pipeline.
**Precondition**: the user authorizes the remote repository; anti-leak review (DEPLOYMENT.md §checklist) executed.
**Gate 14**: green pipeline on PRs; badges in the README; no secrets in workflows.

## Phase 15 — AWS IaC (Terraform) — requires an account/cost authorization
**Goal**: Terraform modules: VPC, subnets, SG, ECR, ECS/Fargate, ALB, RDS PostgreSQL, SQS+DLQ, least-privilege IAM, Secrets Manager, CloudWatch; reviewed `plan`; budget alarm.
**Gate 15**: clean, reviewed `terraform plan`; estimated cost documented; `terraform destroy` tested.

## Phase 16 — Cloud deployment
**Goal**: the `dev` environment operating on AWS (deploy via CI or documented manual steps), migrations applied by a one-off task, CloudWatch alerts (5xx, p95, DLQ > 0, outbox lag).
**Gate 16**: the E2E flow executed in the cloud with the simulator hosted on ECS; an alert fired on purpose and recorded; environment destroyed at the end (cost).

## Phase 17 — Admin/Ops UI (Blazor)
**Goal**: screens for orders, payments, deliveries, webhooks, outbox, DLQ, integrations; requeue/cancel/reconcile actions; Operator/Admin authorization.
**Gate 17**: an operator requeues a failed message without touching the database; tests of the main components (bUnit) and of authorization.

## Phase 18 — Performance & resilience tests
**Goal**: load (k6 or NBomber — to be decided) on `POST /orders` and webhooks; chaotic simulator; measure p95, error rate, outbox lag; adjustments.
**Gate 18**: a report with real numbers (nothing invented), bottlenecks identified and handled or recorded.

## Phase 19 — Documentation hardening
**Goal**: final public README, diagrams, reviewed ADRs, language decision (the public documentation was moved to English on 2026-09-18, ahead of this phase), reading guide for recruiters.
**Gate 19**: review of "everything the project claims is true".

## Phase 20 — Portfolio release
**Goal**: final anti-leak review, `v1.0.0` tag, publication (with authorization).
**Gate 20**: publication checklist (DEPLOYMENT.md) 100%; no private file in the Git history.
