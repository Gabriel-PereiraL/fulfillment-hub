# INTEGRATIONS — external providers (simulated)

> **This project does not connect to Uber infrastructure or to any real payment provider.**
> **The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.**
> No real credentials, no real deliveries, no real charges. Nothing here should be read as a professional or
> commercial integration with Uber or any other supplier.

## 1. Delivery provider: reference contract (Uber Direct API)

### 1.1 Sources consulted

| Source | URL | Consulted on | Notes |
|---|---|---|---|
| Uber Direct — overview | https://developer.uber.com/docs/deliveries/overview | 2026-09-18 | the portal is a SPA; the technical content comes from the pages below |
| Uber Direct — Get started (auth, flow) | https://developer.uber.com/docs/deliveries/get-started | 2026-09-18 | token, `customer_id`, steps |
| Delivery Status Webhook (DaaS) | https://developer.uber.com/docs/deliveries/daas/references/api/webhooks/delivery-status-webhook | 2026-09-18 | `event.delivery_status`, payload, statuses |
| Webhooks — security/retry (general Uber guide) | https://developer.uber.com/docs/riders/guides/webhooks | 2026-09-18 | HMAC-SHA256 hex, retry/backoff, no ordering guarantee |
| Official SDK `uber/uber-direct-sdk` (Apache-2.0) — `src/deliveries/openapi.yaml` (Direct API v1.0.1) and `src/auth/openapi.yaml` | https://github.com/uber/uber-direct-sdk | 2026-09-18 (last repository commit: 2024-10-24) | private copy kept out of the repository for reference; OpenAPI 3.1 spec with schemas and errors |

The primary reference for schemas and error codes is the OpenAPI spec of the official SDK (maintained by Uber). Where the
portal and the spec differ, the portal (more recent) wins and the difference is noted here.

### 1.2 Authentication (real)

- `POST https://auth.uber.com/oauth/v2/token` (portal) / `https://login.uber.com/oauth/v2/token` (SDK spec) — `application/x-www-form-urlencoded`:
  `client_id`, `client_secret`, `grant_type=client_credentials`, `scope=eats.deliveries`.
- Response: `access_token`, `token_type=Bearer`, `expires_in` (portal: 2 592 000 s = 30 days), `scope`.
- Every call: `Authorization: Bearer <token>`; base `https://api.uber.com/v1`; `customer_id` in the path.
- Sandbox: "Test mode" credentials in the dashboard do not create real deliveries.

### 1.3 Endpoints (real) — Direct API

| Operation | Method and path | Request (main fields) | Response (main fields) |
|---|---|---|---|
| Create Quote | `POST /customers/{customer_id}/delivery_quotes` | **`pickup_address`**, **`dropoff_address`** (structured addresses as JSON strings), `pickup_latitude/longitude`, `dropoff_latitude/longitude`, `pickup_ready_dt`, `pickup_deadline_dt`, `dropoff_ready_dt`, `dropoff_deadline_dt` (RFC 3339), `pickup_phone_number`, `dropoff_phone_number`, `manifest_total_value` (cents), `external_store_id` | `kind=delivery_quote`, `id` (`dqt_…`), `created`, **`expires`**, `fee` (cents), `currency` (`brl`), `currency_type` (`BRL`), `dropoff_eta`, `duration` (min), `pickup_duration` (min), `dropoff_deadline` |
| Create Delivery | `POST /customers/{customer_id}/deliveries` | **`pickup_name`, `pickup_address`, `pickup_phone_number`, `dropoff_name`, `dropoff_address`, `dropoff_phone_number`, `manifest_items[]`** (`name`, `quantity`, `size` `small|medium|large|xlarge`, `dimensions`, `price`, `weight`, `must_be_upright`), `quote_id`, `manifest_reference`, `manifest_total_value`, `pickup_notes`, `dropoff_notes`, `deliverable_action`, `undeliverable_action`, `requires_dropoff_signature`, `requires_id`, `tip`, **`idempotency_key`** ("persists for a set time frame, defaulting to 60 minutes"), `external_id`, `external_store_id`, `*_dt` windows | `id` (`del_…`), `quote_id`, **`status`** (`pending`, `pickup`, `pickup_complete`, `dropoff`, `delivered`, `canceled`, `returned`), `complete`, `courier` (`name`, `rating`, `vehicle_type`, `phone_number`, `location{lat,lng}`, `img_href`), `courier_imminent`, `created`, `updated`, `currency`, `fee`, `tracking_url`, `pickup`/`dropoff` (waypoints), `pickup_eta`, `dropoff_eta`, `pickup_ready/deadline`, `dropoff_ready/deadline`, `manifest`, `manifest_items`, `external_id`, `live_mode`, `undeliverable_action`, `undeliverable_reason`, `related_deliveries`, `uuid`, `batch_id` |
| Get Delivery | `GET /customers/{customer_id}/deliveries/{delivery_id}` | — | same shape as `DeliveryResp` |
| List Deliveries | `GET /customers/{customer_id}/deliveries` | filters/pagination | list |
| Update Delivery | `POST /customers/{customer_id}/deliveries/{delivery_id}` | `dropoff_notes`, `pickup_notes`, `manifest_reference`, `tip_by_customer`, verifications, `dropoff_latitude/longitude` | `DeliveryResp` |
| Cancel Delivery | `POST /customers/{customer_id}/deliveries/{delivery_id}/cancel` | — | cancelled delivery (`status=canceled`) |
| Proof of Delivery | `POST /customers/{customer_id}/deliveries/{delivery_id}/proof-of-delivery` | | image/data |

### 1.4 Errors (real) — shape `{ "code": "...", "message": "...", "kind": "error", "metadata"?: {...} }`

| HTTP | `code` | Meaning | Our reaction |
|---|---|---|---|
| 400 | `invalid_params`, `address_undeliverable`, `unknown_location`, `pickup_window_too_small`, `dropoff_deadline_too_early`, `dropoff_deadline_before_pickup_deadline`, `dropoff_ready_after_pickup_deadline`, `pickup_ready_too_early`, `pickup_deadline_too_early`, `pickup_ready_too_late`, `address_undeliverable_limited_couriers`, `expired_quote`, `used_quote`, `mismatched_price_quote`, `not_allowed`, `noncancelable_delivery`, `max_tip_exceeded`… | contract/rule error | **no retry**; `expired_quote`/`used_quote` → requote (max. 2); `noncancelable_delivery` → business failure |
| 401 | `unauthorized` | invalid credential | no retry; renew the token once if expired |
| 402 | `customer_suspended`, `missing_payment` | account | no retry; operational alert |
| 403 | `customer_blocked` | account | no retry; alert |
| 404 | `customer_not_found`, `delivery_not_found` | resource | no retry (on a `GET` right after creation: one short retry, may be eventual) |
| 408 | `request_timeout` | provider timeout | retry (creation only with an `idempotency_key`) |
| 409 | `duplicate_delivery` (`metadata.delivery_id`) | an identical active delivery already exists | **no retry**; `GET` the returned id and reconcile |
| 429 | `customer_limited` | account limit | retry honouring `Retry-After` (when present); metric |
| 500 | `internal_server_error`, `unknown_error` | provider failure | bounded retry (backoff + jitter) |
| 503 | `service_unavailable`, `couriers_busy`, `robo_couriers_busy` | unavailable / no courier | retry with a longer backoff; persistent `couriers_busy` → business failure (`DeliveryFailed`) |

Numeric rate limits **are not publicly documented** in the sources consulted; the simulator applies a configurable limit
to exercise the 429 path.

### 1.5 Status webhook (real) — `event.delivery_status`

- Sent to the `webhook_url` configured in the dashboard as a JSON `POST`. Signature headers: **`X-Uber-Signature`** and/or
  **`X-Postmates-Signature`** = HMAC-SHA256 in **hexadecimal** over the raw body, key = the webhook *signing key*
  (Uber Direct uses one key per webhook, distinct from the client secret — source: webhooks guide; check the dashboard).
- Payload (DaaS portal): `id` (event id), `kind=event.delivery_status`, `created`, `status`, `delivery_id`, `customer_id`,
  `account_id`, `developer_id`, `live_mode`, `batch_id`, `route_id`, `data{...}` = the full delivery object (`id`,
  `status`, `courier`, `courier_imminent`, `pickup_eta`, `dropoff_eta`, `fee`, `tracking_url`, `complete`, `manifest`,
  `pickup`, `dropoff`, `return`…). (There is also the legacy `dapi.status_changed` webhook with `event_id`, `event_time`
  and `meta.status` in UPPER CASE — **not** reproduced.)
- Statuses: `pending`, `pickup`, `pickup_complete`, `dropoff`, `delivered`, `canceled`, `returned` (+ `shopping_completed`, out of scope).
- Guarantees: **may be sent more than once and ordering is not guaranteed**; deduplicate by `id`.
- Provider retry: if the URL answers ≠ 2xx or is unreachable → exponential backoff (30 s multiplier), up to 7 attempts in about an hour.
- Expected response: a quick `200` with an empty body.

## 2. Delivery simulator: `FulfillmentHub.ProviderSimulator` (routes `/delivery/v1/...`)

### 2.1 REAL PROVIDER CONTRACT vs LOCAL SIMULATOR

| Aspect | Real contract (Uber Direct) | Local simulator | Reproduced? |
|---|---|---|---|
| Base URL | `https://api.uber.com/v1` | `http://localhost:5100/delivery/v1` | path/shape yes; host no |
| Auth | OAuth2 client credentials, `eats.deliveries`, Bearer | `POST /delivery/oauth/token` (client_credentials, configurable fake credentials) → Bearer with a short `expires_in` to exercise renewal | partial (no real OAuth) |
| `customer_id` in the path | yes | yes (`cus_sim_…`) | yes |
| Create Quote | request/response above | same field names for the subset: `pickup_address`, `dropoff_address`, `*_dt`, `manifest_total_value` → `id dqt_`, `expires`, `fee`, `currency`, `dropoff_eta`, `duration`, `pickup_duration` | subset |
| Create Delivery | request/response above | subset: required fields + `quote_id`, `idempotency_key`, `external_id`, `manifest_items` (no dimensions/verifications) → `id del_`, `status`, `tracking_url`, `fee`, `courier`, `*_eta` | subset |
| Get / Cancel Delivery | yes | yes (`noncancelable_delivery` after `pickup_complete`) | yes |
| List / Update / Proof of Delivery | yes | **no** | no |
| Errors | table 1.4 | same `code`/HTTP for the subset: `invalid_params`, `address_undeliverable`, `expired_quote`, `used_quote`, `duplicate_delivery` (409 + metadata), `customer_limited` (429 + Retry-After), `internal_server_error`, `couriers_busy`, `service_unavailable`, `request_timeout`, `unauthorized`, `noncancelable_delivery`, `delivery_not_found` | subset |
| `idempotency_key` | 60 min | 60 min (configurable) | yes |
| Webhook | `event.delivery_status`, HMAC hex in `X-Uber-Signature`/`X-Postmates-Signature`, unordered, with duplicates, 7 retries | same event/shape for the subset (`id`, `kind`, `created`, `status`, `delivery_id`, `data{id,status,courier,courier_imminent,tracking_url,fee,pickup_eta,dropoff_eta,complete}`), same signature and headers, **configurable duplicate/delay/out-of-order scenarios**, retry with backoff | subset |
| Real courier, map, tracking page | yes | fictional `tracking_url`; fake courier with a random location | no |
| Official sandbox | yes | not used | — |

### 2.2 Simulated lifecycle

`pending` → (after the courier-assignment delay) `pickup` → `pickup_complete` → `dropoff` → `delivered`; branches: `canceled`
(cancel before `pickup_complete`, or scenario), `returned` (scenario). Every transition produces a webhook. State lives in
memory (D-43).

### 2.3 Scenario configuration (environment variables / `appsettings`)

> Original Phase 0 design. The implementation (Phases 5–6) uses .NET configuration sections instead of `SIM_*` variables:
> generic chaos lives in `Simulator:Chaos:*`, payments in `Simulator:Payments:*` (§3) and deliveries in `Simulator:Delivery:*` (§2.4).

| Variable | Effect | Default |
|---|---|---|
| `SIM_LATENCY_MS` / `SIM_LATENCY_JITTER_MS` | artificial base latency / jitter on every route | 50 / 50 |
| `SIM_FAILURE_RATE` | fraction (0–1) of random 500 responses | 0 |
| `SIM_TIMEOUT_RATE` | fraction of requests that "vanish" (never answer until the client times out) | 0 |
| `SIM_RATE_LIMIT_PER_MINUTE` | above this → 429 + `Retry-After` | 0 (off) |
| `SIM_FORCE_STATUS` | forces a status code for the next N requests (`503:3`) — simulator admin route | — |
| `SIM_COURIERS_BUSY_RATE` | fraction of quotes/creations answered with 503 `couriers_busy` | 0 |
| `SIM_QUOTE_TTL_SECONDS` | quote validity (`expires`) | 900 |
| `SIM_DELIVERY_STEP_MS` | interval between status transitions | 3000 |
| `SIM_WEBHOOK_DUPLICATE_RATE` | fraction of webhooks sent twice | 0 |
| `SIM_WEBHOOK_OUT_OF_ORDER` | `true` shuffles the sending order of consecutive events | false |
| `SIM_WEBHOOK_DELAY_MS` | delay before sending webhooks | 0 |
| `SIM_WEBHOOK_FAIL_FIRST_N` | the first N webhook deliveries "fail" (forces the simulator's retry) | 0 |
| `SIM_WEBHOOK_SIGNING_KEY` | HMAC key (dev only, not a real secret) | dev value |
| `SIM_IDEMPOTENCY_TTL_MINUTES` | `idempotency_key` TTL | 60 |

A simulator admin route (`POST /admin/scenario`) would allow changing the scenario at runtime (E2E tests and demos) — not implemented yet (BL-066).

### 2.4 Implementation (Phase 6, 2026-09-18)

Routes (`FulfillmentHub.ProviderSimulator/Deliveries`), sharing the chaos pipeline of the payment routes (`Simulator:Chaos`,
429 with code `customer_limited`):

| Operation | Method/path | Notes |
|---|---|---|
| Token | `POST /delivery/oauth/token` (form: `client_id`, `client_secret`, `grant_type=client_credentials`, `scope`) | `{ access_token, token_type: "Bearer", expires_in, scope }`; wrong credentials → `401 { error: "invalid_client" }`; opaque token with `TokenLifetimeSeconds` (300 — deliberately short, exercises renewal) |
| Quote | `POST /delivery/v1/customers/{customer_id}/delivery_quotes` | `pickup_address`/`dropoff_address` are **JSON strings** of `{ street_address[], city, state, zip_code, country }` (as in the real contract); response `{ kind: "delivery_quote", id: "dqt_…", created, expires, fee, currency: "brl", currency_type: "BRL", dropoff_eta, duration, pickup_duration, dropoff_deadline }` |
| Create delivery | `POST …/deliveries` | `quote_id`, `idempotency_key`, `external_id`, `manifest_items[]`, names/phones/addresses; response `{ kind: "delivery", id: "del_…", quote_id, status, complete, courier, courier_imminent, created, updated, currency, fee, tracking_url, pickup_eta, dropoff_eta, external_id, manifest_reference, live_mode: false, uuid, undeliverable_reason }` |
| Get / cancel | `GET …/deliveries/{id}`, `POST …/deliveries/{id}/cancel` | cancel only in `pending`/`pickup`; afterwards → `400 noncancelable_delivery` |
| Errors | `{ code, message, kind: "error", metadata? }` | `400 invalid_params / address_undeliverable / expired_quote / used_quote / noncancelable_delivery`, `401 unauthorized`, `404 customer_not_found / delivery_not_found`, `409 duplicate_delivery` (+ `metadata.delivery_id`; same `idempotency_key` within the TTL **or** an `external_id` with an active delivery), `429 customer_limited` (+ `Retry-After`), `503 couriers_busy` (`CouriersBusyRate`), `500` (chaos) |

Lifecycle: `pending` → (`CourierAssignMs`) `pickup` (fictional courier assigned) → (`StepMs`) `pickup_complete` → `dropoff` →
`delivered`; every transition emits an `event.delivery_status` (`{ id, kind, created, status, delivery_id, customer_id,
live_mode, data: <delivery> }`) signed in **`X-Uber-Signature`** (HMAC-SHA256 hex) + `X-Timestamp`, sent to
`Simulator:Delivery:WebhookUrl` (Development: `http://localhost:5000/api/v1/webhooks/deliveries`, since Phase 7);
`WebhookDuplicateRate`, `WebhookDelayMs` and `WebhookOutOfOrder` (random extra delays between consecutive events)
reproduce the real provider's weak delivery guarantees.

**Sandbox rules by dropoff zip code** (`zip_code`): starts with `00000` → `address_undeliverable` (quote and creation);
last digits `001` → a quote valid for **1 s** (forces `expired_quote`/requote); `002` → the delivery ends `returned`;
`003` → a delivery **without webhooks** (only reconciliation notices). Deterministic fee per zip code in whole reais
(`BaseFeeCents` + 100 × (digit sum mod 8)) so it never interferes with the payment sandbox amounts.

**Configuration** (`Simulator:Delivery`): `ClientId`, `ClientSecret` (≥ 8), `CustomerId` (`cus_sim_fulfillmenthub`),
`TokenLifetimeSeconds` 300, `WebhookSigningKey` (≥ 16), `WebhookUrl?`, `QuoteTtlSeconds` 900, `CourierAssignMs` 1000,
`StepMs` 3000, `BaseFeeCents` 1200, `CouriersBusyRate` 0, `WebhookDuplicateRate` 0, `WebhookDelayMs` 0,
`WebhookOutOfOrder` false, `IdempotencyTtlMinutes` 60. Dev-only values in `appsettings.Development.json`.
Not implemented: admin route `POST /admin/scenario` (BL-066), `WebhookFailFirstN` (BL-245).

**FulfillmentHub side** (`Providers:Delivery:*` + `Fulfillment:Origin:*`): `IDeliveryProviderClient` →
`SimulatedDeliveryProviderClient` with the shared pipeline (§4) on the **outside** and `DeliveryBearerTokenHandler` on the
**inside** (`DeliveryAccessTokenProvider`: token cache, renewal `TokenRefreshSkewSeconds` before expiry, 401 → renew once
and repeat); 4xx errors become `Failure`s with `provider.<code>`, `409` carries `Metadata["delivery_id"]`. Flow: quote at
checkout (D-51, `CheckoutDeliveryQuoter`; provider down → estimated fee) → after `Paid` the Worker (`OrderPaid` outbox
handler, plus the `DeliveryRequestService` sweep as a safety net, D-52/D-68) runs `RequestDeliveryHandler`: reuses the
valid quote, requotes **once** when it expired (a second expiry → `delivery.quote_expired`, the order stays `Paid` for
an operator), creates with a durable `idempotency_key` (`order-{id}-delivery-{n}`), `409 duplicate_delivery` → `GET` and
adopt, permanent rejection (`address_undeliverable`, `invalid_params`) → order `Cancelled(DeliveryFailed)` + stock
released. Cancelling an order with an active delivery cancels at the provider first; `noncancelable_delivery` →
`409 order.delivery_in_progress`.

## 3. Simulated payment provider (routes `/payments/v1/...`) — IMPLEMENTED (Phase 5, 2026-09-18)

A **minimal contract of its own**, inspired by the common PSP lifecycle (intent → authorization → capture → refund) —
it does **not** model any specific PSP. Authentication: `Authorization: Bearer <api key>` (`Simulator:Payments:ApiKey`);
JSON in `snake_case`.

| Operation | Method/path | Request | Response |
|---|---|---|---|
| Create payment | `POST /payments/v1/payments` (header `Idempotency-Key` **required** → `400 invalid_request` without it) | `amount` (cents), `currency`, `order_reference`, `customer_reference`, `capture` (bool, default true), `scenario?` (`approve`/`decline`/`silent_approve`, manual tests only) | `201` `{ id: "pay_…", status: "pending", amount, currency, order_reference, failure_code, created_at, updated_at }`; same key + same body → `200` with the same payment; same key + different body → `409 idempotency_conflict` |
| Get | `GET /payments/v1/payments/{id}` | — | same, `status` ∈ `pending/authorized/paid/failed/refunded`, `failure_code?` |
| Refund | `POST /payments/v1/payments/{id}/refunds` | `amount?` (cents; default = remaining balance) | `{ id: "ref_…", payment_id, status: "succeeded", amount, created_at }`; payment not `paid` → `422 not_refundable` |
| Webhook | `POST <Simulator:Payments:WebhookUrl>` | `{ "id": "evt_…", "type": "payment.status_changed", "created_at", "data": { "payment_id", "status", "failure_code"?, "order_reference", "occurred_at" } }`; headers `X-Signature` = HMAC-SHA256 hex(raw body, `WebhookSigningKey`), `X-Timestamp` (unix s), `X-Event-Id` | `2xx` expected; otherwise retried after 1 s / 2 s / 4 s and dropped (logged) |
| Errors | `{ code, message, kind: "error" }` — `400 invalid_request`, `401 unauthorized`, `404 not_found`, `409 idempotency_conflict`, `422 not_refundable`, `429 rate_limited` (+ `Retry-After`), `500 internal_server_error` (chaos) | | |

**Settlement**: every payment starts `pending` and, after `SettleDelayMs`, becomes `paid` or `failed`
(`failure_code = DeclineCode`, default `card_declined`) and triggers the webhook.

**Sandbox rules by amount** (like the "magic amounts" of real PSPs; `PaymentSimulatorStore.ScenarioFromAmount`): cents
ending in **99** → declined; ending in **98** → approved **without a webhook** (simulates a lost webhook — reconciliation
has to notice); anything else follows `ApprovalRate`. An explicit `scenario` in the request takes precedence.

**Configuration** (section `Simulator:Payments`, `appsettings` / `Simulator__Payments__*` variables):

| Key | Effect | Default |
|---|---|---|
| `ApiKey` | key expected in `Authorization: Bearer` | empty (required; `appsettings.Development.json` ships a **dev-only** value) |
| `WebhookSigningKey` | webhook HMAC key (≥ 16 chars) | empty (required; dev-only value in Development) |
| `WebhookUrl` | webhook destination; empty = do not send | `http://localhost:5000/api/v1/webhooks/payments` in Development |
| `SettleDelayMs` | time until `paid`/`failed` | 2000 |
| `ApprovalRate` | approved fraction (0–1) outside the amount rules | 1 |
| `DeclineCode` | `failure_code` of declines | `card_declined` |
| `WebhookDuplicateRate` | fraction of webhooks sent twice | 0 |
| `WebhookDelayMs` | delay before sending | 0 |
| `IdempotencyTtlMinutes` | `Idempotency-Key` TTL | 60 |

Generic chaos (section `Simulator:Chaos`, applied to every simulator route): `LatencyMs`, `LatencyJitterMs`, `FailureRate`
(random 500), `TimeoutRate` (the request "vanishes"), `RateLimitPerMinute` (shared fixed window; 0 = off; 429 + `Retry-After`
with each provider's own code — `rate_limited` / `customer_limited`; implemented in Phase 6). State is in memory
(D-P8 resolved); a restart clears everything.

## 4. Resilience policy for outbound calls

| Element | Initial value (adjustable through Options) |
|---|---|
| Total timeout per operation | 15 s (quote/create), 8 s (get/cancel) |
| Timeout per attempt | 5 s |
| Retry | max. 3, exponential with a 500 ms base, jitter (Polly `DelayBackoffType.Exponential` + `UseJitter`) |
| Retry predicate | 408, 429 (with `Retry-After`), 500, 502, 503, 504, `HttpRequestException`, `TimeoutRejectedException`; **never** 400/401/403/404/409/422 |
| Circuit breaker | 50% failures in a 30 s window with at least 10 calls → open for 30 s |
| POST idempotency | `idempotency_key`/`Idempotency-Key` always present on creations |
| Token | in-memory cache with early renewal; 401 → renew once and repeat |
| Telemetry | one span per call (`peer.service`, status, attempt), metrics `provider.request.duration`, `provider.retry.count`, `provider.circuit.state` |

**Implementation (Phase 5 for payments; Phase 6 extracted the pipeline into `AddProviderResilienceHandler<TOptions>`,
shared with the delivery client, with the `ProviderResilienceOptions` base + `CircuitBreakDurationSeconds`)**:
`AddFulfillmentHubPaymentProvider()` — typed client `IPaymentGatewayClient` with `Microsoft.Extensions.Http.Resilience`:
total timeout (`Providers:Payment:TotalTimeoutSeconds`, 15) → retry (`MaxRetryAttempts` 3, base `RetryBaseDelayMs` 500,
exponential + jitter, honours `Retry-After`; predicate exactly as in the table; `0` disables retries) → circuit breaker
(50% / 30 s / min. 10 / open 30 s) → per-attempt timeout (`AttemptTimeoutSeconds`, 5). `HttpClient.Timeout` is infinite:
the pipeline owns the timeouts. The `Idempotency-Key` sent is `Payment.ProviderIdempotencyKey` (stable per payment, so
retries and reconciliation never create a second payment at the provider). Transport failures, timeouts and an open
circuit become `Failure.Unavailable("provider.unavailable")`; 4xx contract errors become `Validation/Forbidden/NotFound/Conflict`
and are **not** retried. Token: not applicable to payments (static API key); for deliveries, `DeliveryAccessTokenProvider` +
`DeliveryBearerTokenHandler` (Phase 6, §2.4). Telemetry: spans `Provider CreatePayment/GetPayment/RefundPayment` + the
standard `HttpClient` instrumentation (duration/status per attempt); dedicated retry/circuit metrics are left for
Phase 11 (BL-246). Evidence: `PaymentGatewayClientTests` (T9/T10).

## 5. Inbound webhook ingestion

1. Read the raw body (buffered) → verify the HMAC signature (constant-time comparison) and the timestamp window (5 min) → 401 when invalid.
2. Extract `provider_event_id`; `INSERT` into `webhook_events` (`UNIQUE`) → duplicate: immediate `200` (metric `webhook.duplicate`).
3. Phase 9 ✔: publish a pointer `{webhookEventId, provider}` to `fh-webhooks-inbound` and answer `200`; without a broker (or if
   publishing fails) process in-process within the same request (Phases 5–8), always answering `200` regardless of the outcome.
4. Worker (`WebhooksInboundConsumer` → `WebhookEventProcessor` → the provider's `IWebhookProcessor`) applies the event to the
   aggregate with the ordering rules (DOMAIN.md §7) and marks it `Processed/Ignored/Failed`; an event already handled is
   acknowledged without work (redelivery is safe); `Failed` is recorded on the event and reconciliation corrects the state.
5. Webhook endpoint: no JWT authentication (the signature authenticates), its own rate limit, maximum body size (64 KB),
   the full body is never logged (ids/status only).

**Implementation (Phase 5, `POST /api/v1/webhooks/payments`)**: steps 1, 2 and 5 as described (`WebhookSignatureVerifier`:
`CryptographicOperations.FixedTimeEquals`, window `Providers:Payment:WebhookTimestampToleranceSeconds` = 300; `WebhookInbox`
writes in its own DI scope before processing; rate limit policy `webhooks` per IP; body > 64 KB → 413). Step 3 at the
time: in-process processing in the same request (`ApplyPaymentWebhookHandler`); `200` is returned even when processing
fails — the event is marked `Failed` with `last_error` and **reconciliation** (Worker) corrects the state by querying the
provider. Unknown event types or unknown payments → `Ignored`. **D-P5 resolved = yes**: `paid`/`refunded` are confirmed
with a `GET` on the provider before being applied; the provider's status wins over the webhook body
(`Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted`).

**Implementation (Phase 7, `POST /api/v1/webhooks/deliveries`)**: the receiving pipeline was extracted into `WebhookReceiver`
(Api) and is shared by both endpoints — each one only declares its `WebhookSource` (provider, signature header, key,
tolerance) and how to identify/process the payload. Deliveries: header **`X-Uber-Signature`**, key
`Providers:Delivery:WebhookSigningKey`, payload `event.delivery_status` (`id`, `kind`, `created`, `status`, `delivery_id`,
`data{id,status,updated,courier{name,vehicle_type,phone_number,location},tracking_url}`); dedup by `id`;
`ApplyDeliveryWebhookHandler` finds the `Delivery` by `(provider, provider_delivery_id)` and `DeliveryStatusApplier` applies
it through `Delivery.ApplyProviderEvent` — the disposition (`Applied/Duplicate/OutOfOrder/Stale/Conflict`, DOMAIN.md §7)
decides whether the order follows (`pickup_complete`/`dropoff` → `InDelivery`, `delivered` → `Delivered` (through
`InDelivery` when the pickup event was lost), `canceled`/`returned` → `Cancelled(DeliveryFailed)` + stock released).
Out-of-order events count in `fh.webhooks.out_of_order{disposition}`. **D-59**: unlike payments, a delivery status is
**not** confirmed with a `GET` before being applied (no money involved; the aggregate's ordering rules bound the damage).
Step 4 (worker): `ReconcileDeliveriesHandler` re-reads active deliveries without any event for
`Worker:DeliveryReconciliation:QuietForSeconds` (300) and applies the current status as a synthetic event
`reconciled:<id>:<status>:<updated>` through the same rules (`LostWebhooks_AreRecoveredByReconciliation`). Simulator:
`Simulator:Delivery:WebhookUrl` enabled in Development; zip code `…003` = a silent delivery (no webhooks) to exercise reconciliation.

## 6. Queue vs. synchronous call (documented trade-off)

| Operation | Mode | Why |
|---|---|---|
| Delivery quote when placing the order | synchronous (Phase 6 ✔) | the user needs the fee to confirm; provider down → estimated fee (D-51), never a 503 |
| Create payment | asynchronous (outbox `OrderPlaced` → SQS `fh-domain-events` → Worker, Phases 8–9 ✔) | never block `POST /orders`; retries without the customer waiting |
| Create delivery | asynchronous (outbox `OrderPaid` → SQS → Worker, Phases 8–9 ✔; the Worker sweep as a safety net) | only after payment; long retries |
| Process webhooks | persist → `fh-webhooks-inbound` → Worker (Phase 9 ✔; in-process as fallback); the follow-up effects (refund, delivery) leave through outbox events | answer the provider in milliseconds; the `200` does not depend on processing; reconciliation covers failures |
| Cancel a delivery (operator/customer) | synchronous (Phase 6 ✔: cancels at the provider before cancelling locally; `noncancelable_delivery` → 409) | whoever cancels waits for the confirmation |
| Refund a payment | asynchronous (outbox `OrderCancelled`/`PaymentPaid` → SQS → Worker, Phases 8–9 ✔) | never inside a webhook; retried until the provider accepts |
| Reconciliation | periodic job | sweep of pending items |
