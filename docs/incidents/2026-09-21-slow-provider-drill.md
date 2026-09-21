# Drill 2026-09-21 — alerts end to end on the local stack

Executed on 2026-09-21 (12:18–12:55 UTC) against the local stack described in [OBSERVABILITY.md §8](../OBSERVABILITY.md):
`docker compose --profile deps up -d` (PostgreSQL, LocalStack, `grafana/otel-lgtm`), the three hosts started from their
Release builds in the `Development` environment (SQS mode on, D-70), metrics exported every 10 s. Nothing was mocked:
the alerts below fired from the metrics the application already emits, evaluated by the provisioned Grafana rules in
[`observability/grafana/provisioning/alerting/fulfillmenthub-alerts.yaml`](../../observability/grafana/provisioning/alerting/fulfillmenthub-alerts.yaml).

All values were captured with `scripts/alerts-status.sh`, the Prometheus HTTP API (`:9090`) and the Grafana datasource
proxy for Tempo and Loki (`:3000/api/datasources/proxy/uid/{tempo,loki}`). Times are UTC.

## Drill 1 — RB-2 "customers report slow checkout" (ApiHighLatencyP95)

| Step | What happened | Evidence |
|---|---|---|
| Condition | 12:18:52 — the provider simulator was restarted with `Simulator__Chaos__LatencyMs=3000` (every provider endpoint answers after 3 s). `scripts/place-orders.sh 45 0.3` from 12:19 to 12:21:49: 20 × `201`, 25 × `409` (stock exhausted), **every request ≈ 3.2 s** (`POST /api/v1/orders -> 201 (3303 ms)`). | `drill-orders.log` (script output) |
| Metric | `histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket{job="fulfillmenthub-api"}[2m])))` at 12:20:58 → `/api/v1/orders/` = **4.875 s** (bucket interpolation between 2.5 s and 5 s); webhooks routes stayed at 0.05–0.29 s. | Prometheus `/api/v1/query` |
| Rule | `ApiHighLatencyP95` (p95 of `/api/*` over 2 min > 0.8 s, `for: 1m`) — `activeAt 2026-09-21T12:20:30Z`, observed **firing at 12:20:41** (1 min 49 s after the condition started). | `scripts/alerts-status.sh` → `ApiHighLatencyP95  firing  health=ok`; Grafana alertmanager API: `"alertname":"ApiHighLatencyP95","runbook":"RB-2","severity":"medium","state":"active","startsAt":"2026-09-21T12:20:30.000Z"` |
| Investigation — trace | Tempo search `{resource.service.name="fulfillmenthub-api" && name="POST /api/v1/orders/" && duration>2s}` → e.g. trace `4808dc968c80cd86e641065ec78bbe7f` (3020 ms). Spans: `POST /api/v1/orders/` **3021 ms** → `Provider CreateQuote` **3002 ms** → simulator `POST /delivery/v1/customers/{customerId}/delivery_quotes` **3001 ms**; the five `postgresql` spans took 1–4 ms. The time is entirely inside the synchronous delivery quote. | Tempo `/api/traces/4808dc968c80cd86e641065ec78bbe7f` |
| Investigation — logs | Loki `{service_name=~"fulfillmenthub-.*"} \| trace_id = "4808dc968c80cd86e641065ec78bbe7f"` → 10 lines from `fulfillmenthub-api`, all carrying `CorrelationId=01a0c3e9-9127-7421-9bfe-02280ea3edd3`, `RequestPath=/api/v1/orders`: Polly `Execution attempt … Result: '200' … Execution Time: 3001,87ms` (`PipelineName=IDeliveryProviderClient-delivery-provider`, `Attempt=0`), HttpClient `Received HTTP response headers after 3001.7478ms - 200` for `…/delivery_quotes`. No retry, no error: the provider is *slow*, not failing. | Loki `query_range` |
| Cause | Provider latency leaks into `POST /orders` because the quote is synchronous at checkout (D-51). Confirmed by the span tree and the HttpClient timing. | — |
| Action | 12:22:43 — simulator restarted without chaos (`LatencyMs=0`). 12 control requests → ≈ 120 ms each. | `place-orders.sh 12 0.5` |
| Resolution | `ApiHighLatencyP95` back to `inactive` by 12:25:38 (2-minute rate window drained). | `scripts/alerts-status.sh` |
| Follow-up | Already on record: the quote timeout is bounded by the provider pipeline (5 s per attempt) and falls back to the estimated fee; a shorter checkout budget / asynchronous quote stays a BACKLOG discussion (Phase 18 measures it). | `OBSERVABILITY.md §7 RB-2` |

## Drill 2 — RB-6 "worker is dead" (WorkerHeartbeatMissing)

| Step | What happened | Evidence |
|---|---|---|
| Condition | 12:22:42 — the Worker process was stopped. | process list |
| Metric | `sum(increase(fh_worker_heartbeats_total{job="fulfillmenthub-worker"}[2m])) or vector(0)` fell from 8 to 0. | dashboard panel "Worker heartbeats / 2 min" |
| Rule | `WorkerHeartbeatMissing` (< 1 heartbeat in 2 min, `for: 1m`) — **firing at 12:25:38** (≈ 3 min after the stop: 2-min window + 1-min hold). | `scripts/alerts-status.sh` → `WorkerHeartbeatMissing  firing  health=ok` |
| Action | 12:26:29 — Worker restarted (`Heartbeat` log lines resume immediately). | worker log |
| Resolution | `inactive` at the next snapshot (12:34:52). | `scripts/alerts-status.sh` |

## Drill 3 — RB-5 "provider outage → messages in the DLQ" (QueueDlqNotEmpty)

| Step | What happened | Evidence |
|---|---|---|
| Condition | 12:26:29 — the provider simulator was **stopped** (both payment and delivery APIs refuse connections); 16 orders placed at 12:27 (`201`). | `place-orders.sh 16 0.3` |
| What the system did | The outbox published every `OrderPlaced` to SQS successfully (rows end as `Processed`, 0 retries — that is the SQS-mode design, D-70). The consumer's `CreatePayment` handler failed on every receive: `Result: 'No connection could be made because the target machine actively refused it. (localhost:5100)', Handled: 'True'` (Polly retry, then `Unavailable` → `Retry` with `ChangeMessageVisibility` backoff, D-75). `fh_queue_messages_failed_total{queue="fh-domain-events",reason="handler"}` grew by 41 in 15 min; message age p95 reached 72 s at 12:35. After `MaxReceiveCount = 5` the redrive policy parked the messages: `fh_queue_dlq_depth{queue="fh-domain-events"} = 16`. | Prometheus queries; worker log; `awslocal sqs get-queue-attributes` (DLQ `ApproximateNumberOfMessages: 16`) |
| Rule | `QueueDlqNotEmpty` (`max(fh_queue_dlq_depth) > 0`, `for: 1m`) — **firing at 12:44:18** (the rule was provisioned at 12:43 during this drill, see note). | `scripts/alerts-status.sh` → `QueueDlqNotEmpty  firing  health=ok` |
| Cause | Payment provider unreachable for longer than the redelivery budget (5 receives with exponential backoff). Visible in one line of the worker log and in `fh.queue.messages.failed{reason="handler"}`. | — |
| Action | 12:44:43 — simulator restored; DLQ redriven with `awslocal sqs start-message-move-task --source-arn arn:aws:sqs:us-east-1:000000000000:fh-domain-events-dlq`. DLQ depth → 0 within 45 s; 27 orders reached `Delivered` within 3 minutes; the rest sat in visibility backoff (`ApproximateNumberOfMessagesNotVisible: 9`, exponential up to 300 s) and drained progressively — at 12:55, 38 of the 41 orders of the last 45 minutes had a payment (38 payments for 38 orders, **zero duplicates** despite the redeliveries: persisted dedup in `processed_messages`) and 3 were still waiting on a backoff extended by Drill 4 (their redelivery hit the database outage). | LocalStack attributes; `orders`/`payments` counts |
| Resolution | `QueueDlqNotEmpty` `inactive` at 12:45:47. | `scripts/alerts-status.sh` |

**Note on the rule set.** The fifth rule was first written as *message age p95 > 60 s*; it was correct while the consumer
was still retrying (72 s at 12:35) but had nothing to measure once the messages were in the DLQ, and it goes *pending*
again right after a redrive (old `SentTimestamp`). `QueueDlqNotEmpty` is the deterministic form of the same incident
and is what stayed provisioned (D-79 amended). Message age remains a documented early-warning candidate.

## Drill 4 — RB-1 "database outage" (ApiHigh5xxRate)

| Step | What happened | Evidence |
|---|---|---|
| Condition | 12:47:54 — `docker compose --profile deps stop postgres`; 40 × `GET /api/v1/products` (authenticated) over ~90 s, plus the simulator's delivery webhooks arriving in the meantime. | drill script output |
| What the system did | Every DB-backed request → **`500`** ProblemDetails (40/40) with `EventId 1000 Unhandled exception for GET /api/v1/products` (`NpgsqlException`, 174 connection failures logged); `/health/ready` → **`503`**; `/health/live` stayed 200; webhooks `POST /api/v1/webhooks/deliveries` also answered 500 (the inbox row cannot be written — the provider retries, by contract). | API log; Prometheus `increase(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m])` → `/api/v1/products/` 500 = 39.8, `/api/v1/webhooks/deliveries` 500 = 14.3, `/health/ready` 503 = 1.0 |
| Metric | 5xx share = **100 %** (`100 * 5xx / all` over 2 min at 12:52:17). | Prometheus query |
| Rule | `ApiHigh5xxRate` (> 2 % over 2 min, `for: 1m`) — **firing at 12:52:16**. `ApiHighLatencyP95` fired as well: with the database down each request waits for the Npgsql connection attempts (seconds), a correct correlated signal. | `scripts/alerts-status.sh` |
| Cause | Database unavailable — the same second, `/health/ready` reports it; the trace of any failing request ends in a `postgresql` span with an exception event. | — |
| Action | 12:52:17 — `docker compose --profile deps start postgres`; `/health/ready` back to 200 at 12:52:20; 30 control requests → 200. | drill script output |
| Resolution | `ApiHigh5xxRate` `inactive` at 12:54:40 (2-minute window drained). | `scripts/alerts-status.sh` |

## What was not exercised

- `OutboxBacklog` needs the broker to be unavailable (`docker compose --profile deps stop localstack`) or
  `Messaging:Sqs:Enabled=false` with a provider outage; not provoked in this session. Its query was validated against
  the live metric (`fh_outbox_pending` = 0 under normal operation, state `inactive`, `health=ok`).

## Appendix — Kestrel body limit (SECURITY.md §1.7, D-83)

Against the running API (12:18 UTC): `POST /api/v1/orders` with a 300,021-byte JSON body → **`413`**
`{"title":"Content Too Large","status":413,…}`; with a 200,021-byte body → `400` validation problem (the body was
read and validated). The first attempt with `Kestrel:Limits:MaxRequestBodySize` only in `appsettings.json` returned
`400` for the 300 KB body — Kestrel does not bind `Limits` from configuration — which is why `Program.cs` applies the
value with `ConfigureKestrel`.
