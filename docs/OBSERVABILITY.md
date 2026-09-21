# OBSERVABILITY — FulfillmentHub

Goal: be able to answer "what happened to order X?" and "why is the API slow?" without opening the database, using
**structured logging, metrics and correlated distributed tracing** (OpenTelemetry). Local since Phase 1; AWS in Phase 16.

## 1. Stack (ADR-009)

| Signal | Library | Local | AWS |
|---|---|---|---|
| Logs | `Microsoft.Extensions.Logging` + `LoggerMessage` (source generator) + `OpenTelemetry.Exporter.OpenTelemetryProtocol` (logs) + JSON console | Loki via `grafana/otel-lgtm` (OTLP) + stdout | CloudWatch Logs (ADOT collector sidecar or the awslogs driver with JSON) |
| Traces | `OpenTelemetry.Instrumentation.AspNetCore`, `.Http`, `Npgsql` (native, via `Npgsql.OpenTelemetry`), `AWSSDK` instrumentation, our own `ActivitySource` | Tempo via `grafana/otel-lgtm` | AWS X-Ray via ADOT |
| Metrics | `OpenTelemetry.Instrumentation.Runtime`, native ASP.NET Core/HttpClient metrics (.NET 8+ `Meter`), our own `Meter`, Polly metering | Prometheus (OTLP receiver) + Grafana alert rules via `grafana/otel-lgtm` | CloudWatch Metrics (EMF via ADOT) + alarms |
| Health | `Microsoft.Extensions.Diagnostics.HealthChecks` (+ Npgsql, custom SQS) | `/health/live`, `/health/ready` | ALB target group health + ECS |

Why not Serilog: the built-in logging with `LoggerMessage` is already structured, fast and integrates with OTel without
adapters; one less dependency to justify. Local backend: the Aspire Dashboard served Phases 1–9 (one container, zero configuration); the hardening track
replaced it with `grafana/otel-lgtm` (D-78, ADR-009 addendum) because alerting needs a rule engine and persisted
metrics. The Aspire Dashboard stays available as the opt-in `aspire` compose profile.

## 2. Correlation

- `X-Correlation-Id`: accepted from the client (validated: ≤ 64 chars, alphanumeric/`-`) or generated; returned in the response; included as a log scope and as an attribute of the root span.
- `trace_id`/`span_id` (W3C `traceparent`) enter the logs automatically (OTel logs) — the correlation id is a convenience for humans and clients; the trace id is the technical key.
- Asynchronous propagation: `traceparent` is stored in `outbox_messages.trace_parent` and as an SQS message attribute; the consumer creates the span with `ActivityContext.Parse` (link or parent — decision: **parent** for the continuous flow of one order, link when a batch mixes orders).
- `order_id`, `payment_id`, `delivery_id`, `provider_event_id` as span attributes and log fields (never personal data).
- **Proven end to end** (BL-127, `TraceContinuityTests`): a `traceparent` sent to `POST /orders` is the trace id of the `PlaceOrder` span, of the outbox row, of the Worker-side `Outbox OrderPlaced` consumer span, of `CreatePayment`/`Provider CreatePayment` and of the simulator's own server span for `POST /payments/v1/payments`. The provider's *webhook* back to the API starts a new trace (it is a new inbound request from another system); the two are joined by `order_id`/`payment_id` attributes and by the correlation id stored on the inbox row.

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
| `fh.order.time_to_final` | histogram (s) | `final_status` (`Delivered`, `Cancelled`) | Application (`OrdersMetrics.OrderReachedFinalStatus`, called at every final transition: delivery applier, delivery request rejection, payment failure, customer cancellation) | 11 ✔ |
| `fh.idempotency.hits` | counter | `outcome` (`replayed`, `conflict`, `mismatch`) | Api (`IdempotencyMetrics`, in `IdempotencyFilter`) | 11 ✔ |
| `fh.stock.reservation_conflicts` | counter | `kind` (`insufficient_stock`, `concurrent_update`) | Application (`OrdersMetrics`) | 4 ✔ |
| `fh.provider.request.duration` | histogram | `provider`, `operation`, `status_code`, `attempt` | Infrastructure | 5 — covered by `http.client.request.duration` (OTel `HttpClient` instrumentation, tag `http.request.resend_count` = attempt) + the `Provider <op>` span; a dedicated metric only if the standard one is not enough (BL-246) |
| `fh.provider.retries` | counter | `provider`, `operation`, `reason` | Polly telemetry | 11 (BL-246; Polly already emits `resilience.polly.strategy.events` today) |
| `fh.provider.circuit_state` | gauge (0/1/2) | `provider` | Polly telemetry | 11 (BL-246) |
| `fh.webhooks.received` / `.rejected` / `.duplicates` / `.out_of_order` | counter | `provider`, `event_type` (received), `reason` (rejected), `disposition` (out_of_order: `OutOfOrder`/`Stale`) | Api/Application (`WebhooksMetrics`) | 5/7 ✔ |
| `fh.deliveries.events` | counter | `disposition` (`Applied`, `Duplicate`, `OutOfOrder`, `Stale`, `Conflict`) | Application (`DeliveryStatusApplier`) | 7 ✔ |
| `fh.payments.settled` | counter | `status` (`paid`, `failed`, `paid_after_cancellation`) | Application (`PaymentStatusApplier`) | 5 ✔ |
| `fh.deliveries.quotes` / `fh.deliveries.requested` | counter | `outcome` (`quoted`, `fallback_fee`, `rejected`, `requoted` / `created`, `adopted`, `adopted_duplicate`, `deferred`, `rejected`, `quote_expired_twice`) | Application (`DeliveriesMetrics`) | 6 ✔ |
| `fh.webhooks.processing.duration` | histogram (ms) | `provider`, `outcome` (`Processed`, `Ignored`, `Failed`) | Infrastructure (`WebhookEventProcessor`, whether reached from the queue consumer or the in-process fallback) | 11 ✔ |
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
| `PlaceOrder`, `CancelOrder`, `CreatePayment`, `RequestDelivery`, `ApplyPaymentWebhook`, `ApplyDeliveryWebhook` | Application (`ApplicationTelemetry.ActivitySource`) | `order.id`, `customer.id` (id, not name), `order.items.count`, `delivery.provider_id` — Phase 11 ✔ (BL-122) |
| `Outbox <type>` | Infrastructure (`OutboxProcessor`, runs in the Worker) | `messaging.message.id`, `outbox.attempt`; `ActivityKind.Consumer` with parent = the `trace_parent` stored on the message (the handler span continues the trace of the request that produced the event) — Phase 8 ✔ |
| `<queue> publish` / `<queue> receive` | Infrastructure/Worker | `messaging.system=aws_sqs`, `messaging.destination.name`, `messaging.message.id`, `messaging.receive_count`; `receive` continues the trace of the `traceparent` sent as a message attribute (BL-088 ✔ Phase 9); SDK instrumented by `OpenTelemetry.Instrumentation.AWS` |
| `Provider <op>` | Infrastructure | `peer.service=uber-like-simulator`, `provider.operation`, `provider.error.code`, `retry.attempt` — Phase 5 ✔ payment: `Provider CreatePayment/GetPayment/RefundPayment`; Phase 6 ✔ delivery: `Provider CreateQuote/CreateDelivery/GetDelivery/CancelDelivery` (`peer.service=uber-like-simulator`) |
| `Webhook.Ingest` | Api (`WebhookReceiver`) | `webhook.provider`, `webhook.event.type`, `webhook.duplicate` — Phase 11 ✔ |
| DB | Npgsql, automatic | summarized statement (no values) |

## 6. Alerts — Implemented locally (2026-09-21, D-78/D-79)

Five rules are **provisioned from a file** into the local Grafana (`grafana/otel-lgtm`) and evaluated every 30 s against
Prometheus: [`observability/grafana/provisioning/alerting/fulfillmenthub-alerts.yaml`](../observability/grafana/provisioning/alerting/fulfillmenthub-alerts.yaml).
Every rule reads a metric the application already emitted before this track — nothing was instrumented for the alert.
Thresholds below are the **local drill values** (short windows so a scenario fires in 1–3 minutes); the production
value is what a CloudWatch alarm will use in Phase 16 (BL-126). State: `scripts/alerts-status.sh` or Grafana → Alerting.

| Rule | Signal (PromQL, Prometheus names) | Threshold · window · hold (local → production) | Why this alert | Impact when it fires | Likely causes | First action |
|---|---|---|---|---|---|---|
| **ApiHigh5xxRate** (high, RB-1) | `100 * 5xx rate / all rate` of `http_server_request_duration_seconds_count{job="fulfillmenthub-api"}` | > 2 % · 2 min · 1 min → > 2 % · 5 min · 5 min | customers see errors; the first sign of a dependency or code failure | orders cannot be placed/read; webhooks still answer 200 unless the DB is down | database unavailable (`/health/ready` 503 at the same time), unhandled exception (`EventId 1000`), dependency timeout surfacing as 500 | logs filtered by `EventId=1000` + the failing trace; check `/health/ready`; if the DB is down, the worker is failing too |
| **ApiHighLatencyP95** (medium, RB-2) | `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="fulfillmenthub-api",http_route=~"/api/.*"}[2m])))` | > 0.8 s · 2 min · 1 min → > 0.8 s · 5 min · 5 min (`POST /orders` alone > 1.5 s) | checkout latency is the customer-facing SLO | slow checkout; provider timeouts eventually fall back to the estimated fee | slow delivery quote inside `POST /orders` (synchronous, D-51), slow database, provider retries | open a slow `POST /api/v1/orders/` trace → `Provider CreateQuote` span; `http_client_request_duration_seconds` p95 by `server_address` |
| **WorkerHeartbeatMissing** (high, RB-6) | `sum(increase(fh_worker_heartbeats_total{job="fulfillmenthub-worker"}[2m])) or vector(0)` | < 1 · 2 min · 1 min → < 1 · 3 min · 2 min | the worker is the only thing that moves an order after checkout | outbox, queue consumers and reconciliation stop: paid orders are not sent to delivery | process/container dead, hung startup (bad connection string, queue provisioning failed), host out of memory | worker process/container status and last log lines; `fh_outbox_pending` will start climbing |
| **OutboxBacklog** (high, RB-4) | `max(fh_outbox_pending{job="fulfillmenthub-worker"})` | > 10 · 1 min → > 500 · 5 min, or lag p95 > 60 s | messages accepted by the API are not leaving the database | every new order's payment/delivery is delayed | broker (SQS) unavailable so the publisher retries; database slow; with `Messaging:Sqs:Enabled=false`, an in-process handler retrying against a dead provider | `fh_outbox_published_total{outcome="retried"\|"failed"}` by `type`; `GET /api/v1/admin/outbox` for `last_error`; broker health |
| **QueueDlqNotEmpty** (high, RB-5) | `max(fh_queue_dlq_depth{job="fulfillmenthub-worker"})` | > 0 · 1 min → > 0 · 1 min (same) | a message exhausted its receives: something is stuck for real | the affected orders do not progress until the DLQ is redriven | provider down/rejecting longer than the redelivery budget (`MaxReceiveCount` × backoff), poison message, handler bug | `fh_queue_messages_failed_total` by `reason`, worker logs for the failing handler; fix the cause, then redrive (`awslocal sqs start-message-move-task --source-arn <dlq arn>` locally) |

Documented but **not provisioned** (no runbook/owner yet, D-79): provider circuit open for > 2 min, rejected webhooks
> 20 / 5 min (possible attack or wrong key), login failures > 100 / 5 min per IP, queue message age p95 > 60 s (early
warning of the DLQ case — it goes pending again after a redrive because of the old `SentTimestamp`, hence not a rule),
AWS budget > 80 %. Notification channel: none locally (the state is read from Grafana's UI/API); SNS → e-mail in Phase 16.

## 7. Runbooks

Executed and evidenced on 2026-09-21 — see [`incidents/2026-09-21-slow-provider-drill.md`](incidents/2026-09-21-slow-provider-drill.md)
for the real timestamps, queries, trace ids and log lines of RB-2, RB-6, RB-5 and RB-1.

**RB-1 — 5xx rate** (executed: database outage): `/health/ready` → 503 confirms the dependency; API logs `EventId 1000`
show `NpgsqlException`; the worker's heartbeats continue but its jobs log errors; action = restore the database; the
rule resolves once the 2-minute window drains. Nothing to replay: `POST /orders` failed before any write.

**RB-2 — "customers report slowness when creating orders"** (executed: slow provider)
1. Metric: `ApiHighLatencyP95` fires; the dashboard panel "API p95 by route" shows `/api/v1/orders/` at ~4.9 s while
   the webhook routes stay < 0.3 s.
2. Trace: Grafana → Explore → Tempo, `{resource.service.name="fulfillmenthub-api" && name="POST /api/v1/orders/" && duration>2s}`
   → the `Provider CreateQuote` span holds ~100 % of the request; `postgresql` spans are milliseconds.
3. Logs: Explore → Loki, `{service_name=~"fulfillmenthub-.*"} | trace_id = "<id>"` → the HttpClient/Polly lines give
   the exact provider latency, with `CorrelationId` for the customer's ticket.
4. Cause: the delivery quote is synchronous at checkout (D-51); provider latency leaks 1:1 into the response.
5. Action: provider side (here: remove the injected latency). Product decision on record: keep the synchronous quote with
   the estimated-fee fallback; revisit the checkout budget with Phase 18 measurements.

**RB-4 — Outbox backlog**: heartbeat present? (otherwise RB-6); `fh_outbox_published_total{outcome="retried"}` by type
→ which handler; `last_error` in `/api/v1/admin/outbox`; broker reachable? (`docker compose ps localstack`); requeue
`Failed` rows from the admin endpoint after the cause is fixed.

**RB-5 — Messages in the DLQ** (executed: provider outage): `fh_queue_messages_failed_total{reason}` and the worker log
identify the handler; inspect one message (`awslocal sqs receive-message` on the DLQ) — payload bug vs. dependency; fix
the cause; redrive; confirm `fh_queue_dlq_depth` = 0 and the orders progress; dedup (`processed_messages`) guarantees no
double effect on redelivery.

**RB-6 — Worker without heartbeat** (executed: process stopped): restart the host; check the outbox/queues drained;
if the process is alive but silent, capture a dump/log level bump before restarting.

Post-incident notes go to `docs/incidents/` (timeline, cause, action, follow-ups), as done for the drill above.

## 8. Local stack, dashboard and reproducible scenarios

`docker compose --profile deps up -d` starts PostgreSQL, LocalStack and **`grafana/otel-lgtm` 0.33.1** (OTel Collector
+ Prometheus + Tempo + Loki + Grafana in one container, D-78). The hosts export OTLP gRPC to `localhost:4317`
(`OTEL_EXPORTER_OTLP_ENDPOINT` in `appsettings.Development.json`, metrics every 10 s via `OTEL_METRIC_EXPORT_INTERVAL`).

| URL | What |
|---|---|
| http://localhost:3000 | Grafana (anonymous admin, local only). Dashboards → *FulfillmentHub — Overview* (provisioned from `observability/grafana/dashboards/`); Alerting → Alert rules; Explore → Prometheus / Tempo / Loki |
| http://localhost:9090 | Prometheus API/UI (metrics arrive through the OTLP receiver; names are the translated ones: `http_server_request_duration_seconds_bucket`, `fh_outbox_pending`, `fh_worker_heartbeats_total`, …) |
| http://localhost:18888 | Optional Aspire Dashboard — `docker compose --profile aspire up -d` and point `OTEL_EXPORTER_OTLP_ENDPOINT` at `http://localhost:4327` |

Helpers (Git Bash / WSL / Linux): `scripts/place-orders.sh [count] [pause]` logs in as the seeded customer and places
orders through the real API; `scripts/alerts-status.sh [--watch]` prints rule states from the Grafana ruler API.

### Provoking each alert (existing knobs only, D-80 — no fault injection in the API)

| Scenario | How | Expected |
|---|---|---|
| Slow provider → **ApiHighLatencyP95** | restart the simulator with `Simulator__Chaos__LatencyMs=3000` (env var; every provider endpoint sleeps 3 s), then `scripts/place-orders.sh 40 0.3` | `POST /orders` ≈ 3.2 s; rule fires after ~2 min; trace shows `Provider CreateQuote`. Undo: restart the simulator without the variable |
| Database outage → **ApiHigh5xxRate** | `docker compose --profile deps stop postgres`, then hit any DB-backed endpoint (`GET /api/v1/products` with a token, or `place-orders.sh`) for a minute | 500 ProblemDetails, `/health/ready` 503, rule fires after ~2 min. Undo: `docker compose --profile deps start postgres` |
| Worker down → **WorkerHeartbeatMissing** | stop the Worker process | fires ≈ 3 min after the last heartbeat. Undo: start the Worker |
| Provider outage → **QueueDlqNotEmpty** | stop the simulator entirely, place ≥ 10 orders, wait (5 receives × backoff ≈ 5–8 min) | `fh_queue_messages_failed_total{reason="handler"}` grows, then `fh_queue_dlq_depth` > 0 and the rule fires. Undo: start the simulator and redrive: `docker exec fulfillmenthub-localstack-1 awslocal sqs start-message-move-task --source-arn arn:aws:sqs:us-east-1:000000000000:fh-domain-events-dlq` |
| Broker down → **OutboxBacklog** | `docker compose --profile deps stop localstack`, place ≥ 12 orders | publisher retries, `fh_outbox_pending` > 10, rule fires after ~1.5 min. Undo: start LocalStack; the publisher drains |
| Provider 500s / hangs (no dedicated rule) | `Simulator__Chaos__FailureRate=0.5` or `TimeoutRate=0.3` | retries and circuit breaker visible in `http_client_request_duration_seconds` and the worker logs; falls into the DLQ case if sustained |

The simulator chaos knobs (`Simulator:Chaos:*` — `LatencyMs`, `LatencyJitterMs`, `FailureRate`, `TimeoutRate`,
`RateLimitPerMinute`) are configuration of a development-only host; the API and the Worker have no such switch.

## 9. AWS (Phase 16)
- ADOT collector as a sidecar in each task (Api, Worker) → CloudWatch Logs (one log group per service, 14-day retention in dev), CloudWatch Metrics (namespace `FulfillmentHub`), X-Ray traces.
- CloudWatch dashboards: "API", "Worker/Outbox/Queues", "Providers". Alarms from section 6 with SNS → e-mail.
- Cost-aware: short retention, custom metrics with few dimensions (avoid cardinality explosion), trace sampling (e.g. 20% in dev, 100% of errors).
