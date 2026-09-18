# Current state — FulfillmentHub

> Operational memory of the project. **Always update it at the end of a session.**
> Command for the next session: "Read PROJECT_STATE.md, ROADMAP.md, BACKLOG.md and DECISIONS.md before continuing."

## Last update
2026-09-18 (session 10 — English-only documentation cleanup; no feature work)

## Current phase
**Phase 9 — SQS (LocalStack): COMPLETE (Gate 9 closed).**
Next phase: **Phase 10 — Security hardening** (not started). Public repository at https://github.com/Gabriel-PereiraL/fullfillmentHub (first published in session 9).

## Completed (session 10 — documentation cleanup)
- All public documentation (root README, `docs/*.md`, `docs/adr/*.md`) rewritten in English, preserving meaning, dates, IDs (BL-xxx, D-xx, T-xx) and ADR numbering; D-76 recorded (D-P1 resolved). Stale backlog statuses corrected (BL-028, BL-070, BL-145, BL-202, BL-203). `.gitignore` comments, development seed product names and the fictional store name in `appsettings.json` translated (no contract, route, column, queue, metric or configuration key renamed). No runtime behaviour change; Phase 10 not started.

## Completed (Phase 9)
- Application/Messaging: `Queues` (`fh-domain-events`, `fh-webhooks-inbound`), `MessageEnvelope`, `IMessagePublisher` (`IsEnabled`), `QueueMetrics` (`fh.queue.messages.processed/failed{queue,consumer}`, `fh.queue.message.age`, gauge `fh.queue.dlq.depth`); shared `Webhooks/WebhookOutcome`.
- Infrastructure/Messaging: `SqsOptions` (`Messaging:Sqs`: `Enabled`, `ServiceUrl`, `Region`, credentials only for LocalStack, `QueuePrefix`, `MaxReceiveCount` 5, `VisibilityTimeoutSeconds` 60, `WaitTimeSeconds` 20, `BatchSize` 10, `MaxConcurrency` 4, `RetryBaseDelaySeconds`/`RetryMaxDelaySeconds`), `AddFulfillmentHubMessaging()` (`IAmazonSQS`, `SqsQueueProvisioner` — DLQ + queue with `RedrivePolicy`, idempotent, URL cache —, `SqsMessagePublisher` or `NoOpMessagePublisher`), `ProcessedMessage` (`processed_messages` PK `(consumer, message_id)`) + migration `20260918173803_ProcessedMessages` (applied locally); `Outbox/OutboxDispatcher` (handler in its own scope; with dedup: `processed_messages` + effect in the same transaction, rollback on `Retry`, PK race = duplicate); `OutboxProcessor` publishes to SQS when enabled; `Webhooks/IWebhookProcessor` + `WebhookEventProcessor` (marks the inbox once; a non-`Received` event is acknowledged without work); `PaymentWebhookProcessor`/`DeliveryWebhookProcessor` (parsing/application extracted from the endpoints, keyed by provider); `OpenTelemetry.Instrumentation.AWS`. Packages: `AWSSDK.SQS` 4.0.100.14, `Testcontainers.LocalStack` 4.15.0, `OpenTelemetry.Instrumentation.AWS` 1.18.0.
- Worker/Messaging: `SqsConsumer` base (long polling, `SemaphoreSlim`, delete after success, `ChangeMessageVisibility` with backoff, DLQ depth every 30 s, `receive` spans linked to the `traceparent`; logs 8100–8104), `DomainEventsConsumer` (`domain-events` dedup), `WebhooksInboundConsumer` (pointer `{webhookEventId, provider}`; always acknowledges — D-74), `SqsProvisioningService` (before the consumers).
- Api: `WebhookReceiver` persists → publishes a pointer to `fh-webhooks-inbound` → in-process fallback if the broker is disabled/unavailable (log 5203); webhook endpoints reduced to `WebhookSource` + keyed processor. `appsettings.Development.json` (Api/Worker) with `Messaging:Sqs` enabled for LocalStack (`http://localhost:4566`, placeholder credentials `test`). `docker-compose.yml`: `localstack` service (`localstack/localstack:4`, `SERVICES=sqs`, healthcheck) in the `deps` profile; `.env.example` `LOCALSTACK_PORT`.
- Tests: **225 in total**. `SqsApiFixture` (inherits `ApiFixture` through the `ConfigureSettings`/`ConfigureTestServices` hooks; LocalStack Testcontainers; hosted consumers; `MaxReceiveCount=3`, 2 s visibility, 1 s backoff) + `SqsMessagingTests` (3): an order paid and shipped through both queues (`processed_messages` with `OrderPlaced`/`OrderPaid`, webhook `Processed` by the consumer); **T14** redelivery of the same envelope → 1 payment; **T15** `NoSuchEvent` envelope → `receive 3/3` → DLQ. The main suite still runs with messaging disabled (in-process).
- Manual verification with the full compose (Postgres + LocalStack + Aspire) and the three hosts: queues created by the Worker, `Created → AwaitingPayment → DeliveryRequested → InDelivery → Delivered` in 16 s, 4 delivery webhooks applied by the Worker (0 inside the API request), 0 publish failures, 0 errors, 0 secrets. Build 0 warnings; format clean; 0 vulnerabilities.

## Completed (Phase 8)
- Domain: `IAggregateRoot` (non-generic view of the events), `PaymentPaid` event (raised in `Payment.ApplyProviderStatus(Paid)`).
- Application/Outbox: `IOutboxHandler` + `OutboxHandler<TEvent>` + `OutboxHandling.Done/Retry`; handlers `OrderPlaced` → `CreatePaymentForOrderHandler`, `OrderPaid` → `RequestDeliveryHandler`, `OrderCancelled` and `PaymentPaid` → `RefundPaymentHandler` (new; closes BL-244); registered by key `AddKeyedScoped<IOutboxHandler, T>(nameof(Event))`; `OutboxMetrics` (`fh.outbox.published{outcome,type}`, `lag`, `publish.duration`, `pending/failed` gauges).
- Infrastructure/Outbox: `OutboxMessage` (`outbox_messages`: jsonb payload, `aggregate_id`, `attempts`, `next_attempt_at`, `locked_until`, `last_error`, `correlation_id`, `trace_parent`, index `(status, next_attempt_at)`, xmin) + migration `20260918170717_OutboxMessages` (applied to the local database); `OutboxInterceptor` (`SaveChangesInterceptor` in `AddDbContext`: events → rows in the same commit, cleared after the commit; request correlation id and `Activity.Id`); `OutboxEventSerializer` (STJ, strongly-typed ids as GUID, `Money` `{amount,currency}`, registration by CLR name); `OutboxProcessor` (`SELECT …, xmin … FOR UPDATE SKIP LOCKED` + `Outbox:LeaseSeconds` lease, one DI scope per message, exponential backoff with jitter `BaseDelaySeconds`/`MaxDelaySeconds`, `Failed` after `MaxAttempts`, `Outbox <type>` span with parent = `trace_parent`; logs 7000–7002); `AddFulfillmentHubOutboxPublisher()`.
- Worker: `OutboxPublisherService : PeriodicJob` (`Worker:Outbox:IntervalMs` 500); `DeliveryRequestService` kept at 60 s as a safety net (D-68).
- Api: `POST /orders` answers `Created` (the payment leaves through the outbox; D-64); `Admin/OutboxAdminEndpoints` (`GET /api/v1/admin/outbox?status=`, `POST /api/v1/admin/outbox/{id}/retry`, AdminOnly, log 7100); `RateLimitOptions` (`RateLimiting:LoginPerMinute` 5, `WebhooksPerMinute` 1200 — D-69, found by the suite: 120/min dropped delivery webhooks).
- Tests: **222 in total** at the end of Phase 8. New `OutboxTests` (6): T12 (message written in the same commit with the correlation id, no effect during the request, published later; provider down on the 1st publish → `Pending`/`attempts=1`, success later), T13 (3 failures → `Failed`, listed in the admin, requeue → processed), admin for Admin only, redelivery of a processed message → 1 payment, cancelling a paid order → refund through the outbox. Fixture: pausable publisher hosted in the test host (`OutboxControl.Pause()/RunOnceAsync()`, starts paused until the migrations), `Outbox:MaxAttempts=3`/`BaseDelaySeconds=1`, `CircuitBreakDurationSeconds=1` on both providers; Phase 5–7 tests adapted to the asynchronous triggers (`PlacePaidOrderAsync` pauses the outbox and publishes `OrderPlaced` by hand).
- Manual verification on Kestrel: `Created` (t+0) → `AwaitingPayment` (t+3 s) → `Paid` (t+5 s) → `DeliveryRequested` (t+6 s) → `InDelivery` → `Delivered` (t+18 s) through the outbox + webhooks alone; the admin lists `OrderPlaced/OrderPaid/PaymentPaid/OrderDeliveryRequested/OrderDelivered` processed and no `Failed`; 0 errors, 0 secrets. ADR-004 with an "Implementation" section.

## In progress
- Nothing.

## Next tasks (Phase 10 — Security hardening; first read SECURITY.md in full)
1. Threat model reviewed against what exists (webhooks, outbox/SQS, admin); OWASP Top 10 checklist filled in item by item with a link to code/test (A01 broken access control — existing cross-access tests + admin; A02; A03 injection — parameterized EF, `FromSqlInterpolated`; A04; A05 headers/CORS; A07 rate limiting; A08 signatures/outbox; A09 logs).
2. Security headers (`X-Content-Type-Options`, `Referrer-Policy`, minimal CSP for Scalar in dev), explicit CORS (no origin by default), request size limits (Kestrel `MaxRequestBodySize`), `ForwardedHeaders` documented (BL-106).
3. PII redaction in logs (masked e-mail/phone; review the existing `LoggerMessage`s), no webhook/order body in logs (already), `EnableSensitiveDataLogging` never outside Development.
4. Audit: `dotnet list package --vulnerable` as a documented step (already clean), mass assignment review (request DTOs already without server fields), progressive login lockout (P2, decide), HMAC key rotation (P2, decide).
5. Tests: broken access control (customer on admin/operator routes; operator on admin; token with a tampered role), headers present, CORS denied, body > limit → 413.
6. Docs: SECURITY.md "implemented" in every section, DECISIONS, BACKLOG (BL-10x), ROADMAP; close Gate 10.

## Blockers
- None. Docker Desktop must be running for the integration tests.

## Decisions taken in sessions 9–10 (DECISIONS.md D-64…D-76)
- Phase 8: D-64 own outbox; D-65 `Done`/`Retry`/`Failed`/requeue; D-66 refund from two events; D-67 STJ serialization; D-68 sweep kept; D-69 configurable webhook rate limit. Phase 9: D-70 `Messaging:Sqs:Enabled` mode key; D-71 queues created by the Worker; D-72 dedup in the same transaction; D-73 webhook pointer + fallback; D-74 pointer always acknowledged; D-75 visibility backoff. Session 10: D-76 public documentation in English.

## Pending decisions
D-P2 separate Admin · D-P4 k6/NBomber · D-P6 AWS dev network · D-P7 Terraform state.

## Current tests
- 225 green tests (125 unit, 5 architecture, 95 integration; ~1.5 min warm — two fixtures with containers: Postgres and Postgres+LocalStack). TEST_STRATEGY matrix: T1–T18 ✔ (T19 end-to-end trace → Phase 11; T20 chaos → Phase 12).

## Current infra
- Local: database with 6 migrations (`OutboxMessages`, `ProcessedMessages` applied); compose with Postgres + LocalStack (SQS) + Aspire Dashboard and seed; Api/Worker user-secrets: `Database:ConnectionString`, `Jwt:SigningKey` (Api), `Seed:*Password` (Api), `Providers:Payment:*`, `Providers:Delivery:*`. Remote `origin` = https://github.com/Gabriel-PereiraL/fullfillmentHub (`main` branch); no AWS account/resources.

## Next gate
**Gate 9 — closed.** Criteria: poison message → DLQ after `maxReceiveCount` ✔ (T15); a consumer receives a duplicate and does not duplicate the effect ✔ (T14); the full compose works ✔ (smoke test with Postgres + LocalStack + Aspire + 3 hosts); tests with Testcontainers LocalStack ✔; BACKLOG P0 closed ✔ (BL-084…087, 147).
**Gate 10 — Security hardening**: OWASP checklist with a link to code/test for every item; broken access control tests; SECURITY.md without "planned" sections for what exists.

## Command for the next session (planned resumption: Monday, 2026-09-21)
"Read PROJECT_STATE.md, ROADMAP.md, BACKLOG.md and DECISIONS.md before continuing." — then start with task 1 of the list above (Phase 10), with Docker Desktop open and `docker compose --profile deps up -d` (Postgres + LocalStack + Aspire; the volumes persist, the database already has the 6 migrations and the seed).

State at the end of 2026-09-18: clean working tree, local `main` = `origin/main`, no host running, compose containers stopped (`docker compose --profile deps stop`). No work in progress and no parallel branch. Push only with the user's explicit authorization.
