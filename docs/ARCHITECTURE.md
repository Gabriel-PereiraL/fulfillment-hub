# ARCHITECTURE — FulfillmentHub

## 1. Style: modular monolith, three processes

One product, one solution, one database. Three processes because they have different lifecycles and scaling profiles —
not because "microservices look good":

| Process | Project | Why it is separate |
|---|---|---|
| **API** | `FulfillmentHub.Api` | serves HTTP (customers, admin, webhooks); scales per request; must answer fast |
| **Worker** | `FulfillmentHub.Worker` | outbox publisher, SQS consumers, reconciliation; scales with the backlog; can restart without taking the API down |
| **Provider Simulator** | `FulfillmentHub.ProviderSimulator` | stands in for **external** systems; has to be a separate process so the HTTP integration is real (network, timeouts, failures) |

Everything shares `Domain`, `Application` and `Infrastructure` — except the Simulator, which is deliberately independent
so no FulfillmentHub internals leak into the "provider".

What does **not** exist, and why (ADR-001): microservices (operational cost with no benefit for a team of one), Kubernetes
(ECS Fargate covers it), Kafka (SQS covers it with far less operation), service mesh, a dedicated API gateway, CQRS with
separate databases, event sourcing.

## 2. Solution and projects

```
FulfillmentHub.slnx
├── Directory.Build.props            # Nullable, ImplicitUsings, TreatWarningsAsErrors, analyzers, LangVersion
├── Directory.Packages.props         # Central Package Management
├── .editorconfig
├── docker-compose.yml               # postgres, localstack (SQS), aspire-dashboard (OTLP)
├── src/
│   ├── FulfillmentHub.Domain/           # entities, value objects, domain events, domain exceptions. No framework packages.
│   ├── FulfillmentHub.Application/      # use cases, DTOs, ports (interfaces to the outside), IFulfillmentHubDbContext, Result
│   ├── FulfillmentHub.Infrastructure/   # EF Core + Npgsql, migrations, outbox, SQS, provider HttpClients, auth (JWT/hashing), telemetry
│   ├── FulfillmentHub.Api/              # Minimal APIs, endpoint filters (idempotency), ProblemDetails, OpenAPI, composition root
│   ├── FulfillmentHub.Worker/           # BackgroundServices: outbox publisher, queue consumers, reconciliation
│   ├── FulfillmentHub.ProviderSimulator/# Minimal APIs imitating the providers (delivery "Uber-like" and payment) + webhook delivery
│   └── FulfillmentHub.Admin/            # Blazor Web App (Phase 17, not created yet) — uses Application/Infrastructure directly
└── tests/
    ├── FulfillmentHub.UnitTests/        # domain, provider clients against a scripted transport, retry policies, mappings
    ├── FulfillmentHub.IntegrationTests/ # WebApplicationFactory + Testcontainers (PostgreSQL, LocalStack) + simulator in-process
    ├── FulfillmentHub.ArchitectureTests/# NetArchTest: dependency direction, conventions
    └── FulfillmentHub.E2ETests/         # a few full flows against compose (Phase 12, not created yet)
```

### Dependency direction (enforced by ArchitectureTests)

```
Api ──────► Application ──► Domain
 │              ▲
 ▼              │
Infrastructure ─┘  (Infrastructure implements Application's ports and the DbContext)
Worker ───► Application, Infrastructure
Admin ────► Application, Infrastructure
ProviderSimulator ──► (nothing from FulfillmentHub)
```

Rules:
- `Domain` references no package beyond the BCL.
- `Application` references `Domain` and the `Microsoft.EntityFrameworkCore` package (for `DbSet<T>`/LINQ through
  `IFulfillmentHubDbContext`). This is an accepted, documented dependency (ADR-006): abstracting EF Core away costs
  more than it gives.
- `Infrastructure` references `Application` (implements its ports) — never the other way round.
- Hosts (`Api`, `Worker`, `Admin`) are composition roots: they register modules through extension methods.

## 3. Organization by module

Inside each project, folders follow the **domain module** (lightweight bounded contexts), not the technical type:

```
Domain/
  Common/        Entity, AggregateRoot, IDomainEvent, DomainException, Money, Address, strongly-typed ids
  Catalog/       Product
  Customers/     Customer (+ saved addresses)
  Orders/        Order, OrderItem, OrderStatus, OrderStatusChange, events (OrderPlaced, OrderPaid, OrderCancelled...)
  Payments/      Payment, PaymentAttempt, PaymentStatus, events
  Deliveries/    Delivery, DeliveryQuote, DeliveryEvent, DeliveryStatus
  Identity/      User, Role

Application/
  Common/        Result, Failure, IFulfillmentHubDbContext (time comes from TimeProvider, no IClock)
  Orders/        PlaceOrderHandler, CancelOrderHandler, OrderQueries, DTOs
  Payments/      CreatePaymentForOrderHandler, ApplyPaymentWebhookHandler, ReconcilePaymentsHandler, RefundPaymentHandler, IPaymentGatewayClient
  Deliveries/    CheckoutDeliveryQuoter, RequestDeliveryHandler, ApplyDeliveryWebhookHandler, ReconcileDeliveriesHandler, IDeliveryProviderClient
  Outbox/        IOutboxHandler + handlers per domain event
  Messaging/     IMessagePublisher, queue names, queue metrics
  Catalog/, Customers/, Identity/, Webhooks/

Infrastructure/
  Persistence/   FulfillmentHubDbContext, Configurations/<Module>/, Migrations/, conventions (ids, value objects, xmin)
  Outbox/        OutboxMessage, OutboxInterceptor, OutboxProcessor, OutboxDispatcher, OutboxEventSerializer
  Messaging/     SQS client, queue provisioner, publisher, processed_messages
  Providers/     shared resilience pipeline; Payments/ and Deliveries/ typed clients + webhook processors
  Webhooks/      WebhookEvent, WebhookInbox, WebhookSignatureVerifier, WebhookEventProcessor
  Identity/      PasswordHasher, JwtTokenService
  Telemetry/     ActivitySource, Meter, OpenTelemetry registration
  Idempotency/   IdempotencyRecord store
```

Communication **between modules** inside the monolith:
- Preferably through **domain events via the outbox** (Orders → Payments → Deliveries), which is the product flow anyway.
- Direct calls into another module's use case are allowed when the operation is synchronous by nature (for example
  `PlaceOrder` reads `Catalog` for price and stock in the same commit). No "anti-corruption layer" between internal modules.
- One `DbContext`, one schema (no per-module table prefixes; clear names are enough).

## 4. Patterns adopted and rejected

| Topic | Adopted | Rejected (and why) |
|---|---|---|
| Data access | `IFulfillmentHubDbContext` (DbSets + SaveChanges) used directly by use cases | generic repository / unit of work (they duplicate EF Core), specification pattern |
| Use-case orchestration | explicit classes per use case, plain DI | MediatR/Mediator (a dispatcher without a problem to solve; MediatR went commercial in 2025) |
| API | Minimal APIs, one `MapGroup` per module, `TypedResults`, explicit registration | controllers (no gain here), reflection-based auto-discovery |
| Errors | ProblemDetails (RFC 9457) + `IExceptionHandler`; a small `Result` for expected failures; `DomainException` for invariants | Result on every method; exceptions for control flow |
| Validation | native Minimal API validation (.NET 10, DataAnnotations) + domain invariants | FluentValidation (a dependency without a need) |
| Mapping | explicit methods (`ToResponse()`), LINQ projections | AutoMapper/Mapster |
| Events | domain events → outbox → SQS → worker (Phases 8–9 ✔; in-process when `Messaging:Sqs:Enabled=false`) | in-process handlers with external effects (dual write) |
| Consistency | transactional outbox, idempotent consumers, database constraints | 2PC, sagas with a dedicated orchestrator |
| Concurrency | optimistic (`xmin`) + constraints (`CHECK`, `UNIQUE`) | pessimistic locks by default (only where justified: `SKIP LOCKED` in the outbox) |
| Outbound HTTP | typed `HttpClient` + `Microsoft.Extensions.Http.Resilience` | manual Polly v7, blind retries |
| Logging | `Microsoft.Extensions.Logging` + `LoggerMessage` + OpenTelemetry | Serilog (good, but unnecessary here) |
| Time | injected `TimeProvider` | `DateTime.Now` |
| Configuration | typed Options validated at startup | `IConfiguration["x"]` scattered around |

## 5. Cross-cutting components

### 5.1 Idempotency (ADR-010)
- **API**: `Idempotency-Key` header required on `POST /orders` (and every other mutating POST). Endpoint filter:
  key + scope (user) + body hash → `IdempotencyRecord`. Same key + same hash → stored response is replayed;
  same key + different hash → `422`; key in progress → `409`. Expires after 24 h.
- **Webhooks**: `UNIQUE (provider, provider_event_id)` on `WebhookEvent`; inserted before any effect; duplicate → `200` without reprocessing.
- **Outbound**: `idempotency_key` sent to the delivery provider on creation; derived from the `OrderId` (retries are safe).
- **Consumers** (Phase 9 ✔): `processed_messages (consumer, message_id)` inserted in the same transaction as the effect
  (`OutboxDispatcher`); the webhook consumer uses the `WebhookEvent` status itself.

### 5.2 Transactional outbox (ADR-004)
- Aggregates collect `IDomainEvent`s; a `SaveChangesInterceptor` turns them into `OutboxMessage` rows (type, JSON payload,
  `occurred_at`, trace context) **in the same commit**.
- `OutboxProcessor`/`OutboxPublisherService` (Worker) claims a batch with `FOR UPDATE SKIP LOCKED` + lease, publishes to
  SQS (Phase 9 ✔; in-process handlers when messaging is off) and marks `processed_at`; a failure increments `attempts`
  and schedules `next_attempt_at` with exponential backoff + jitter; after N attempts → `Failed` (visible and
  retryable by an administrator).
- Guarantee: **at-least-once**. Consequence: every consumer is idempotent.

### 5.3 Messaging (ADR-005)
- Standard SQS queues: `fh-domain-events` (outbox output) and `fh-webhooks-inbound` (webhooks accepted by the API for
  asynchronous processing), each with a dead-letter queue.
- Locally: **LocalStack** in compose (profile `deps`). Tests: Testcontainers LocalStack (`SqsMessagingTests`).
  Implementation (ADR-005 "Implementation", Phase 9 ✔): `SqsConsumer` base (long polling, bounded concurrency, delete
  after success, visibility backoff), `DomainEventsConsumer`, `WebhooksInboundConsumer`, `SqsQueueProvisioner`
  (queues + DLQ with redrive policy created at startup).
- When **not** to use a queue: the delivery quote (synchronous, the user waits for the fee), login, queries.
  Documented per operation in INTEGRATIONS.md.

### 5.4 Resilience
- One pipeline per provider (`AddResilienceHandler`): total timeout, retry (exponential + jitter) with a per-status
  predicate (INTEGRATIONS.md §4), circuit breaker, per-attempt timeout.
- Bulkhead: concurrency limit in the consumers (`SemaphoreSlim`), not per provider for now.
- Inbound rate limiting on the API (`AddRateLimiter`): per user/IP on public endpoints and webhooks.

### 5.5 Security (SECURITY.md)
- JWT bearer issued by the API itself; roles/policies; `FallbackPolicy` = authenticated; webhooks authenticated by HMAC
  signature plus a timestamp window.
- Secrets: user-secrets/environment locally; AWS Secrets Manager in the cloud.

### 5.6 Observability (OBSERVABILITY.md)
- OpenTelemetry: traces (ASP.NET Core, HttpClient, Npgsql, AWS SDK, own spans), metrics (runtime, ASP.NET, own: outbox
  lag, retries, webhook failures, provider latency), logs correlated by `trace_id`.
- Locally: Aspire Dashboard (container) receiving OTLP. AWS: ADOT collector sidecar → CloudWatch (logs/metrics) and X-Ray (traces).

## 6. Critical flows (sequences)

### 6.1 PlaceOrder (synchronous, transactional)
1. Idempotency filter (key/scope/hash).
2. Shape validation (DataAnnotations).
3. Handler: loads the customer; quotes the delivery (before the transaction); loads the products (tracked);
   `Order.Place(...)` (invariants); `product.Reserve(qty)` (throws when insufficient → failure `Result`);
   adds `OrderPlaced` to the aggregate.
4. `SaveChangesAsync`: the interceptor writes the outbox row; the product's `xmin` detects a race →
   `DbUpdateConcurrencyException` → the handler retries a few times, then answers `409` (the client repeats with the same key).
5. `201 Created` with `Location`; the response is stored in the `IdempotencyRecord`.

### 6.2 Payment (asynchronous)
1. The Worker consumes `OrderPlaced` → `CreatePaymentForOrderHandler`: creates `Payment(Pending)` + `PaymentAttempt#1`,
   calls `IPaymentGatewayClient.CreateAsync` (idempotent per payment).
2. The provider answers `pending/authorized`; saved; `Order → AwaitingPayment`.
3. Webhook `payment.status_changed` → the API verifies the signature, inserts the `WebhookEvent` (dedup), enqueues it
   and answers `200` quickly.
4. The Worker applies it: `Payment` paid, `Order.MarkAsPaid(...)`, outbox `OrderPaid`. Failure → `Payment.Fail`,
   `Order.Cancel(PaymentFailed)`, stock released.
5. Reconciliation: `Pending` for more than X minutes → `GET` on the provider.

### 6.3 Delivery (asynchronous and resilient)
1. `OrderPaid` → `RequestDeliveryHandler`: reuses the checkout quote while valid, otherwise `POST delivery_quotes`
   (retry matrix) → saves a `DeliveryQuote` (with `expires`).
2. `POST deliveries` with `quote_id` + `idempotency_key` derived from the order → saves `Delivery(Pending)`;
   `Order → DeliveryRequested`. `409 duplicate_delivery` → `GET` and adopt. `expired_quote` → requote (once).
3. Webhooks `event.delivery_status` → dedup → queue → `ApplyDeliveryWebhookHandler`: applies the transition when the
   status is "later" than the current one (canonical order), otherwise records the event as out of order without
   regressing the state.
4. `delivered` → `Order → Delivered`. `canceled/returned` → cancellation and refund rules.

## 7. Structural decisions on record
ADR-001 (modular monolith), ADR-004 (outbox), ADR-005 (SQS), ADR-006 (no repository/MediatR), ADR-007 (Minimal APIs),
ADR-010 (idempotency). Full index in `DECISIONS.md` and `adr/`.
