# OBSERVABILITY — FulfillmentHub

Goal: be able to answer "what happened to order X?" and "why is the API slow?" without opening the database, using
**structured logging, metrics and correlated distributed tracing** (OpenTelemetry). Local since Phase 1; AWS in Phase 16.

## 1. Stack (ADR-009)

| Signal | Library | Local | AWS |
|---|---|---|---|
| Logs | `Microsoft.Extensions.Logging` + `LoggerMessage` (source generator) + `OpenTelemetry.Exporter.OpenTelemetryProtocol` (logs) + JSON console | Aspire Dashboard (OTLP) + stdout | CloudWatch Logs (ADOT collector sidecar or the awslogs driver with JSON) |
| Traces | `OpenTelemetry.Instrumentation.AspNetCore`, `.Http`, `Npgsql` (native, via `Npgsql.OpenTelemetry`), `AWSSDK` instrumentation, our own `ActivitySource` | Aspire Dashboard | AWS X-Ray via ADOT |
| Metrics | `OpenTelemetry.Instrumentation.Runtime`, native ASP.NET Core/HttpClient metrics (.NET 8+ `Meter`), our own `Meter`, Polly metering | Aspire Dashboard | CloudWatch Metrics (EMF via ADOT) + alarms |
| Health | `Microsoft.Extensions.Diagnostics.HealthChecks` (+ Npgsql, custom SQS) | `/health/live`, `/health/ready` | ALB target group health + ECS |

Why not Serilog: the built-in logging with `LoggerMessage` is already structured, fast and integrates with OTel without
adapters; one less dependency to justify. Why the Aspire Dashboard locally: one container, zero configuration, shows all
three signals. Alternative on record: `grafana/otel-lgtm` (Grafana + Tempo + Prometheus + Loki) if persistent dashboards
are needed in Phase 11.

## 2. Correlation

- `X-Correlation-Id`: accepted from the client (validated: ≤ 64 chars, alphanumeric/`-`) or generated; returned in the response; included as a log scope and as an attribute of the root span.
- `trace_id`/`span_id` (W3C `traceparent`) enter the logs automatically (OTel logs) — the correlation id is a convenience for humans and clients; the trace id is the technical key.
- Asynchronous propagation: `traceparent` is stored in `outbox_messages.trace_parent` and as an SQS message attribute; the consumer creates the span with `ActivityContext.Parse` (link or parent — decision: **parent** for the continuous flow of one order, link when a batch mixes orders).
- `order_id`, `payment_id`, `delivery_id`, `provider_event_id` as span attributes and log fields (never personal data).

## 3. Logs

- JSON on the console (container-friendly); levels: `Information` for state transitions and external calls (summary), `Warning` for retries/rejected or duplicate webhooks, `Error` for failures that end in the outbox `Failed` state/DLQ or unhandled exceptions.
- `LoggerMessage` with a fixed `EventId` per message (catalog in `Infrastructure/Telemetry/LogEvents.cs`).
- Forbidden: full request/webhook bodies, tokens, keys, unmasked e-mail/phone, stack traces at `Information`.
- Example fields: `{ "EventId": 2101, "Message": "Order {OrderId} transitioned {From}->{To}", "OrderId": "...", "From": "Paid", "To": "DeliveryRequested", "CorrelationId": "...", "TraceId": "..." }`.

## 4. Metrics (names follow the OTel semantic conventions where they exist)

| Metric | Type | Dimensions | Source | Phase |
|---|---|---|---|---|
| `http.server.request.duration` | histogram | route, status | native ASP.NET Core | 1 |
| `http.client.request.duration` | histogram | `server.address`, status | native HttpClient | 1 |
| `fh.orders.placed` / `fh.orders.cancelled` | counter | `reason` (cancel) | Application (`OrdersMetrics`) | 4 ✔ |
| `fh.order.time_to_final` | histogram (s) | `final_status` | Worker | 11 (BL-123) |
| `fh.idempotency.hits` | counter | `outcome` (`replayed`, `conflict`, `mismatch`) | Api filter | 4 — **pending** (only log `4100` for now; counter to be added in Phase 11) |
| `fh.stock.reservation_conflicts` | counter | `kind` (`insufficient_stock`, `concurrent_update`) | Application (`OrdersMetrics`) | 4 ✔ |
| `fh.provider.request.duration` | histogram | `provider`, `operation`, `status_code`, `attempt` | Infrastructure | 5 — covered by `http.client.request.duration` (OTel `HttpClient` instrumentation, tag `http.request.resend_count` = attempt) + the `Provider <op>` span; a dedicated metric only if the standard one is not enough (BL-246) |
| `fh.provider.retries` | counter | `provider`, `operation`, `reason` | Polly telemetry | 11 (BL-246; Polly already emits `resilience.polly.strategy.events` today) |
| `fh.provider.circuit_state` | gauge (0/1/2) | `provider` | Polly telemetry | 11 (BL-246) |
| `fh.webhooks.received` / `.rejected` / `.duplicates` / `.out_of_order` | counter | `provider`, `event_type` (received), `reason` (rejected), `disposition` (out_of_order: `OutOfOrder`/`Stale`) | Api/Application (`WebhooksMetrics`) | 5/7 ✔ |
| `fh.deliveries.events` | counter | `disposition` (`Applied`, `Duplicate`, `OutOfOrder`, `Stale`, `Conflict`) | Application (`DeliveryStatusApplier`) | 7 ✔ |
| `fh.payments.settled` | counter | `status` (`paid`, `failed`, `paid_after_cancellation`) | Application (`PaymentStatusApplier`) | 5 ✔ |
| `fh.deliveries.quotes` / `fh.deliveries.requested` | counter | `outcome` (`quoted`, `fallback_fee`, `rejected`, `requoted` / `created`, `adopted`, `adopted_duplicate`, `deferred`, `rejected`, `quote_expired_twice`) | Application (`DeliveriesMetrics`) | 6 ✔ |
| `fh.webhooks.processing.duration` | histogram | `provider` | Worker | 7 |
| `fh.outbox.pending` / `fh.outbox.failed` | gauge | — | `OutboxMetrics` (refreshed on every publisher pass) | 8 ✔ |
| `fh.outbox.published` | counter | `outcome` (`processed`, `retried`, `failed`), `type` | `OutboxProcessor` | 8 ✔ |
| `fh.outbox.lag` | histogram (s: `now - occurred_at` at publish time) | `type` | `OutboxProcessor` | 8 ✔ |
| `fh.outbox.publish.duration` | histogram (ms per handler) | `type` | `OutboxProcessor` | 8 ✔ |
| `fh.queue.messages.processed` / `.failed` | counter | `queue`, `consumer`, `reason` (failed) | Worker (`SqsConsumer`) | 9 ✔ |
| `fh.queue.message.age` | histogram (s: `SentTimestamp` → receive) | `queue` | Worker (`SqsConsumer`) | 9 ✔ |
| `fh.queue.dlq.depth` | gauge | `queue` | Worker (`GetQueueAttributes` every 30 s) + native CloudWatch | 9 ✔ /16 |
| `fh.reconciliation.corrections` | counter | `kind` (`payment_status`, `delivery_status`) | Application (`ReconcilePaymentsHandler`, run by the Worker) | 5 ✔ |
| `process.runtime.dotnet.*` (GC, threadpool, exceptions) | various | — | native | 1 |

## 5. Traces (our own spans)

| Span | Where | Attributes |
|---|---|---|
| `PlaceOrder` (and the other use cases) | Application | `order.id`, `customer.id` (id, not name), `order.items.count` |
| `Outbox <type>` | Infrastructure (`OutboxProcessor`, runs in the Worker) | `messaging.message.id`, `outbox.attempt`; `ActivityKind.Consumer` with parent = the `trace_parent` stored on the message (the handler span continues the trace of the request that produced the event) — Phase 8 ✔ |
| `<queue> publish` / `<queue> receive` | Infrastructure/Worker | `messaging.system=aws_sqs`, `messaging.destination.name`, `messaging.message.id`, `messaging.receive_count`; `receive` continues the trace of the `traceparent` sent as a message attribute (BL-088 ✔ Phase 9); SDK instrumented by `OpenTelemetry.Instrumentation.AWS` |
| `Provider <op>` | Infrastructure | `peer.service=uber-like-simulator`, `provider.operation`, `provider.error.code`, `retry.attempt` — Phase 5 ✔ payment: `Provider CreatePayment/GetPayment/RefundPayment`; Phase 6 ✔ delivery: `Provider CreateQuote/CreateDelivery/GetDelivery/CancelDelivery` (`peer.service=uber-like-simulator`) |
| `Webhook.Ingest` | Api | `webhook.provider`, `webhook.event.type`, `webhook.duplicate` |
| DB | Npgsql, automatic | summarized statement (no values) |

## 6. Alerts (planned; implemented in Phase 16 with CloudWatch, simulated locally in Phase 11)

| Alert | Condition | Severity | Action (runbook) |
|---|---|---|---|
| API 5xx rate | > 2% over 5 min | high | RB-1 |
| API p95 latency | > 800 ms over 5 min (`POST /orders` > 1.5 s) | medium | RB-2 |
| Provider circuit open | `fh.provider.circuit_state == open` for > 2 min | medium | RB-3 |
| Outbox lag | p95 > 60 s or `pending` > 500 | high | RB-4 |
| Outbox/webhook failed | `failed` > 0 | medium | RB-4 |
| DLQ | `ApproximateNumberOfMessagesVisible` > 0 (DLQ) | high | RB-5 |
| Worker without heartbeat | no poll metric for 3 min | high | RB-6 |
| Rejected webhooks | > 20 over 5 min | medium (possible attack/wrong key) | RB-7 |
| Login failures | > 100 over 5 min per IP | medium | RB-8 |
| AWS budget | > 80% of the monthly budget | high | destroy/pause the environment |

## 7. Investigation runbook (template; will be executed and evidenced in Phase 11)

**RB-2 — "Customer reports slowness when creating orders"**
1. Metric: `http.server.request.duration` p95 by route → `POST /orders` went from 300 ms to 4 s starting at 14:02.
2. Trace: open a slow `POST /orders` trace → the `Provider CreateQuote` span took 3.5 s with `retry.attempt=2`, `status_code=503`.
3. Provider metrics: `fh.provider.request.duration{provider=uber-like}` p95 went up; `fh.provider.retries{reason=503}` grew; `circuit_state` = half-open.
4. Logs (filtered by `trace_id`): `Warning` "Provider returned 503 couriers_busy, retrying in 1.2 s (attempt 2/3)" — context: `order.id`, `correlation_id`.
5. Alert: "Provider circuit open" fired at 14:05, confirming the impact.
6. Action: because the quote is synchronous inside `POST /orders`, provider slowness leaks to the customer → recorded decision: reduce the total quote timeout to 6 s and return a 503 ProblemDetails with `Retry-After`; evaluate an asynchronous quote (BACKLOG).
7. Post-incident: record it in `docs/incidents/` (to be created in Phase 11) with timeline, cause, action and follow-ups.

**RB-4 — High outbox lag**: check that the worker is alive (heartbeat), `failed` (error in `last_error`), a stuck lock (`SKIP LOCKED` prevents it), a slow database (Npgsql traces), batch size; requeue `Failed` messages from the Admin.

**RB-5 — Messages in the DLQ**: inspect the message (Admin), identify the cause (invalid payload vs. bug), fix, redrive.

## 8. Local (docker compose)
- `aspire-dashboard` (`mcr.microsoft.com/dotnet/aspire-dashboard`) port 18888 (UI) / 18889 (OTLP gRPC). API/Worker/Simulator export through `OTEL_EXPORTER_OTLP_ENDPOINT`.
- Default variables: `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=local`.

## 9. AWS (Phase 16)
- ADOT collector as a sidecar in each task (Api, Worker) → CloudWatch Logs (one log group per service, 14-day retention in dev), CloudWatch Metrics (namespace `FulfillmentHub`), X-Ray traces.
- CloudWatch dashboards: "API", "Worker/Outbox/Queues", "Providers". Alarms from section 6 with SNS → e-mail.
- Cost-aware: short retention, custom metrics with few dimensions (avoid cardinality explosion), trace sampling (e.g. 20% in dev, 100% of errors).
