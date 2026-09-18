# ADR-004 — Transactional outbox

**Status**: accepted · **Date**: 2026-09-18 (implementation: Phase 8)

## Context
After saving an order we need to trigger effects outside the transaction (create the payment, create the delivery, publish to a queue).
Saving to the database **and** publishing to a queue in separate steps (dual write) loses events if the process dies between the two,
or publishes events from transactions that were rolled back.

## Options
1. Best-effort dual write (publish after `SaveChanges`) — unsafe.
2. Two-phase commit / distributed transactions — unavailable/complex with SQS.
3. **Transactional outbox**: the event is written in the same transaction as the aggregate; a separate publisher reads and publishes it.
4. Change Data Capture (Debezium) — far too much infrastructure.

## Decision
Option 3. Aggregates accumulate `IDomainEvent`s; a `SaveChangesInterceptor` serializes them into `outbox_messages` in the same commit.
The `OutboxPublisher` (Worker) reads in batches with `FOR UPDATE SKIP LOCKED`, dispatches (Phase 8: in-process handlers; Phase 9: SQS),
marks `processed_at`; failures → exponential backoff with jitter, `Failed` after N (visible/requeueable in the Admin).

## Rationale
Guarantees at-least-once without extra infrastructure; it is the recognized pattern for this problem; it lets us show retry, a logical DLQ and observability (lag).

## Trade-offs / limitations
- **At-least-once** ⇒ every consumer must be idempotent (ADR-010).
- **Eventual** consistency between modules: an `Order` may stay `Created` for a few seconds before `AwaitingPayment`; documented in the product.
- Polling the outbox adds latency (short interval, e.g. 500 ms) and light load on the database; acceptable.
- Ordering: guaranteed only per aggregate within a batch ordered by `occurred_at`; consumers must not depend on global order.

## Consequences
- No external effect is triggered inside an HTTP request (except explicitly synchronous operations, such as the quote).
- Mandatory tests: zero loss with an injected failure; retry; `Failed`; requeue.

## Implementation (Phase 8, 2026-09-18)

- **Capture**: `AggregateRoot<TId> : IAggregateRoot` accumulates `IDomainEvent`s; `OutboxInterceptor` (`SaveChangesInterceptor`, registered in `AddDbContext`) converts the events of the tracked aggregates into `outbox_messages` rows in the same `SaveChanges` and clears the events only after the commit. `System.Text.Json` serialization (`OutboxEventSerializer`: strongly-typed ids as GUID, `Money` as `{amount, currency}`, enums as text); `type` = the event's CLR name, registry built by reflection over the domain assembly. Columns: `id`, `type`, `payload jsonb`, `aggregate_id`, `occurred_at`, `created_at`, `status`, `attempts`, `next_attempt_at`, `locked_until`, `processed_at`, `last_error`, `correlation_id`, `trace_parent`; index `(status, next_attempt_at)`; `xmin`. Migration `20260918170717_OutboxMessages`.
- **Publishing**: `OutboxProcessor` (Infrastructure) — a short transaction with `SELECT …, xmin FROM outbox_messages WHERE status='Pending' AND next_attempt_at <= now AND (locked_until IS NULL OR locked_until < now) ORDER BY occurred_at LIMIT n FOR UPDATE SKIP LOCKED`, lease (`Outbox:LeaseSeconds`, 60) written and committed; then each message is dispatched in its **own DI scope** to the `IOutboxHandler` registered by key (`AddKeyedScoped<IOutboxHandler, T>(nameof(Event))`), with an `Outbox <type>` span linked to the `trace_parent` of the originating request. `OutboxHandling.Done` → `Processed`; `Retry`/exception → `attempts++`, exponential backoff with jitter (`BaseDelaySeconds` 2, `MaxDelaySeconds` 300), `Failed` after `MaxAttempts` (5). The Worker runs `OutboxPublisherService : PeriodicJob` every `Worker:Outbox:IntervalMs` (500).
- **Consumers** (Application/Outbox/Handlers, all idempotent): `OrderPlaced` → `CreatePaymentForOrderHandler` (reuses the active payment; idempotency key per payment); `OrderPaid` → `RequestDeliveryHandler` (active delivery → `AlreadyRequested`); `OrderCancelled` and `PaymentPaid` → `RefundPaymentHandler` (refunds only if the order is `Cancelled` and the payment `Paid`, key `refund-{paymentId}`) — closes BL-244 (late capture and cancellation after payment). `Unavailable` result → `Retry`; permanent rejections → `Done` (the handler already dealt with the business side).
- **What left the request**: `POST /orders` no longer creates the payment (answers `Created`; D-64); `PaymentStatusApplier` does not call providers. Deliberately still synchronous: the quote at checkout (D-51) and the delivery cancellation at the provider inside `POST /orders/{id}/cancel` (INTEGRATIONS §6).
- **Operations**: `GET /api/v1/admin/outbox?status=Failed` and `POST /api/v1/admin/outbox/{id}/retry` (AdminOnly; `Requeue` resets attempts). Metrics `fh.outbox.published{outcome,type}`, `fh.outbox.lag`, `fh.outbox.publish.duration`, gauges `fh.outbox.pending/failed`. Logs 7000–7002 (publisher), 7100 (requeue).
- **Safety net**: `DeliveryRequestService` (sweep of `Paid` orders without a delivery) and the reconciliations continue, with a longer interval — they cover a message stuck in `Failed` without intervention.
- **Evidence**: `OutboxTests` — T12 (`OrderPlaced` written with the order, the request's correlation id, published later; failure on the first publish → `Pending` with `attempts=1` and a future `next_attempt_at`, success later), T13 (3 failures → `Failed`, listed in the admin, requeue → processed), redelivery of a processed message → a single payment, cancellation of a paid order → refund through the outbox. Kestrel smoke test: `Created → AwaitingPayment → Paid → DeliveryRequested → InDelivery → Delivered` in 18 s through the outbox + webhooks alone.
