# ADR-005 — SQS as the queue (LocalStack locally) and the queue vs. synchronous criterion

**Status**: accepted · **Date**: 2026-09-18 (implementation: Phase 9)

## Context
We need asynchronous processing with retry, DLQ and bounded concurrency for: outbox publishing, webhook ingestion and
long-running tasks. The AWS target asks for SQS. Locally we need something equivalent.

## Options
1. **SQS standard** (+ DLQ), LocalStack locally, Testcontainers in tests.
2. RabbitMQ (great locally; not the AWS target; one more service to operate).
3. Kafka (overkill for the volume and for one developer).
4. Outbox + database polling only, no queue (works, but demonstrates neither messaging nor a real DLQ).

## Decision
Option 1. `fh-domain-events` and `fh-webhooks-inbound` queues, each with a DLQ (`maxReceiveCount` = 5). Consumers in the Worker
with long polling, a `VisibilityTimeout` longer than the processing, bounded concurrency and persisted deduplication.
Before Phase 9 the outbox dispatches in-process (Phase 8) so that the system works without a queue.

## Criterion for using a queue (documented per operation in INTEGRATIONS.md §6)
Use a queue when: the caller does not need the immediate result; there is a long retry; there is an external effect; peaks must be
absorbed (webhooks). Do not use one when: the user is waiting for the answer (quote, login, queries), or when the operation is trivial and local.

## Trade-offs
- SQS standard: at-least-once and unordered ⇒ idempotency and decisions by state (already required by the outbox).
- LocalStack ≠ 100% AWS (small behavioural differences); mitigated by tests in the cloud (Phase 16).
- AWS cost practically zero for the volume.

## Consequences
- `IMessagePublisher`/consumers use the AWS SDK for .NET; OTel instrumentation for SQS; age/DLQ metrics.
- The Admin shows the DLQ and allows redrive.

## Implementation (Phase 9, 2026-09-18)

- **Client and queues**: `AddFulfillmentHubMessaging()` — `Messaging:Sqs` (`Enabled`, LocalStack `ServiceUrl` or empty for AWS, `Region`, static credentials only for LocalStack, `QueuePrefix`, `MaxReceiveCount` 5, `VisibilityTimeoutSeconds` 60, `WaitTimeSeconds` 20, `BatchSize` 10, `MaxConcurrency` 4, backoff `RetryBaseDelaySeconds`/`RetryMaxDelaySeconds`); `IAmazonSQS` singleton; `SqsQueueProvisioner` creates `fh-domain-events`/`fh-webhooks-inbound` and the `-dlq` DLQs with a `RedrivePolicy` (idempotent; on AWS the queues would come from Terraform, Phase 15) and resolves names → URLs. LocalStack (`localstack/localstack:4`, `sqs` only) in `docker-compose.yml`, `deps` profile.
- **Publishing**: `IMessagePublisher` (Application) with `MessageEnvelope {id, type, payload, occurredAt, correlationId, traceParent}`; `SqsMessagePublisher` sends JSON + `type`/`traceparent` attributes; `NoOpMessagePublisher` when `Enabled=false`. The `OutboxProcessor` publishes the envelope to `fh-domain-events` (the outbox row becomes `Processed` once the broker accepted it) or, with messaging disabled, dispatches in-process as in Phase 8 (D-70).
- **Consumers** (Worker, `SqsConsumer` base): long polling, `SemaphoreSlim(MaxConcurrency)`, `DeleteMessage` only after success, `ChangeMessageVisibility` with exponential backoff + jitter on failure (the redrive policy moves the message to the DLQ after `MaxReceiveCount` receives), DLQ depth every 30 s (`fh.queue.dlq.depth`), `Consumer` spans linked to the `traceparent`. `DomainEventsConsumer` → `OutboxDispatcher` with dedup: `processed_messages (consumer, message_id)` inserted **in the same transaction** as the handler's effect (rollback on `Retry`; a PK violation in a race = duplicate). `WebhooksInboundConsumer` → `WebhookEventProcessor` (an event that is no longer `Received` is acknowledged without work).
- **Webhooks**: the API persists to the inbox and publishes a `{webhookEventId, provider}` pointer to `fh-webhooks-inbound`; without a broker (or if `SendMessage` fails) it processes in-process in the same request (log 5203). Per-provider parsing/application moved from the endpoints to `IWebhookProcessor` (`PaymentWebhookProcessor`, `DeliveryWebhookProcessor`, keyed by provider).
- **Metrics/traces**: `fh.queue.messages.processed/failed{queue,consumer}`, `fh.queue.message.age{queue}`, `fh.queue.dlq.depth{queue}`; `OpenTelemetry.Instrumentation.AWS` on the SDK calls.
- **Evidence**: `SqsMessagingTests` (Testcontainers LocalStack): an order paid and shipped through both queues (`processed_messages` with `OrderPlaced`/`OrderPaid`); T14 redelivered message → one payment; T15 poison message → DLQ after 3 receives (log `receive 3/3`). Smoke test with the full compose (Postgres + LocalStack + Aspire) and the three hosts: `Created → … → Delivered` in 16 s; the 4 delivery webhooks were applied by the **Worker** (0 by the API request); 0 errors.
- **Limitations**: no DLQ redrive from the Admin (BL-089, Phase 17); `processed_messages` without purge (P2); FIFO is not used (ordering decided by state + provider timestamp, as before).
