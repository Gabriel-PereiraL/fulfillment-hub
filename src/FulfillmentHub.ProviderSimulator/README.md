# FulfillmentHub.ProviderSimulator

Simulated external providers used by FulfillmentHub for local development, tests and demos.

> **This project does not connect to Uber infrastructure or to any real payment provider.**
> **The simulator reproduces a limited subset of public API contracts for educational and portfolio purposes only.**
> No real deliveries, charges or credentials are involved.

Routes (contracts in `docs/INTEGRATIONS.md`):

- `/payments/v1/...` — minimal payment provider contract (Phase 5, implemented): `POST /payments` (bearer API key,
  mandatory `Idempotency-Key` with replay / `409 idempotency_conflict`), `GET /payments/{id}`,
  `POST /payments/{id}/refunds`. Every payment starts `pending` and settles to `paid` or `failed` after
  `Simulator:Payments:SettleDelayMs`, then a `payment.status_changed` webhook is delivered to
  `Simulator:Payments:WebhookUrl` signed with HMAC-SHA256 (`X-Signature`, `X-Timestamp`, `X-Event-Id`), retried on
  non-2xx (1 s / 2 s / 4 s).
- `/delivery/v1/...` — subset of the Uber Direct API contract (Phase 6, implemented): `POST /delivery/oauth/token`
  (fake client credentials, short-lived opaque tokens), `POST /customers/{id}/delivery_quotes`,
  `POST /customers/{id}/deliveries` (`quote_id`, `idempotency_key`, `external_id` → `409 duplicate_delivery` with
  `metadata.delivery_id`), `GET /customers/{id}/deliveries/{id}`, `POST .../cancel` (`noncancelable_delivery` once the
  courier has the parcel). Deliveries move `pending → pickup → pickup_complete → dropoff → delivered` on a timer
  (`Simulator:Delivery:CourierAssignMs` / `StepMs`) and each transition emits an `event.delivery_status` webhook signed
  in `X-Uber-Signature` when `Simulator:Delivery:WebhookUrl` is set (consumed from Phase 7 on).

  Sandbox rules by dropoff `zip_code`: starting with `00000` → `address_undeliverable`; last digits `001` → 1-second
  quotes (forces `expired_quote`); `002` → the parcel is `returned`. Fees are deterministic whole reais per zip code.

Payment sandbox amounts (like the "magic" test values of real providers): cents ending in `99` are declined
(`card_declined`), cents ending in `98` are approved **without** a webhook (lost-webhook scenario; the API's
reconciliation has to notice). Anything else follows `ApprovalRate`. A `scenario` field (`approve`, `decline`,
`silent_approve`) overrides the amount rule for manual tests.

Chaos (all routes, section `Simulator:Chaos`): `LatencyMs` / `LatencyJitterMs`, `FailureRate` (random 500),
`TimeoutRate` (request never answered), `RateLimitPerMinute` (shared fixed window → 429 + `Retry-After`, with each
provider's own error code: `rate_limited` / `customer_limited`). Delivery-specific: `CouriersBusyRate` (503).

Configuration lives in `appsettings.json` (empty keys) and `appsettings.Development.json` (dev-only API key and
signing key — placeholders, not secrets; the API's user-secrets must use the same values, see `docs/DEVELOPMENT.md`).
State is kept in memory: restarting the simulator forgets every payment, quote and delivery.
