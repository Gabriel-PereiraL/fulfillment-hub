# PRODUCT — FulfillmentHub

## 1. What it is

**FulfillmentHub** is a backend that manages and orchestrates **orders, payments and deliveries**.
It receives customer orders, validates them and reserves stock, creates and confirms the payment with a payment provider,
quotes and requests the delivery from a logistics provider, follows the provider events (webhooks) until a final state,
and exposes an operations view of all of it — including failures, retries and dead-lettered messages.

It is a **portfolio** project built to demonstrate serious backend engineering in **C#/.NET**:
consistency, idempotency, resilience, messaging, security, observability and operations.

> **Mandatory notice (repeated in every public document):**
> This project does not connect to Uber infrastructure or to any real payment provider.
> The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.
> No real deliveries, charges or credentials are involved.

## 2. Why this product

An order → payment → delivery flow concentrates, in a domain everyone understands, almost every engineering problem a
production backend has to solve:

| Real problem | Where it shows up in FulfillmentHub |
|---|---|
| Consistency between the database and external effects | saving the order and publishing the event (transactional outbox) |
| Idempotency | repeated `POST /orders`, duplicate webhooks, retried delivery creation |
| Concurrency | simultaneous stock reservations, duplicate payment confirmations |
| HTTP integrations that fail | slow or unstable delivery provider (timeouts, 429, 5xx) |
| Out-of-order events | a "delivered" webhook arriving before "pickup" |
| Asynchronous processing | worker, SQS queues, retries with backoff, dead-letter queue |
| Security | authentication, role-based authorization, webhook signatures, secrets |
| Observability | one trace following an order across API → queue → worker → provider |
| Operations | an operator sees failures, reprocesses them, investigates incidents |

## 3. Name

Working name, adopted: **FulfillmentHub**. Alternatives considered (Dispatchly, OrderFlow, Fulfilla) were discarded as
less descriptive or too brand-like. The name may be revisited in Phase 19 (documentation hardening) without technical
impact — the `FulfillmentHub.*` root namespaces only change by explicit decision.

## 4. Actors

| Actor | Description | Role |
|---|---|---|
| Customer | places orders, follows their status, cancels while allowed | `Customer` |
| Operator | follows orders/payments/deliveries, reprocesses failures, cancels deliveries | `Operator` |
| Administrator | everything the operator does, plus users, products and settings | `Admin` |
| Payment provider (simulated) | receives payment requests, sends webhooks | external system |
| Delivery provider (simulated, "Uber-like") | quotes, creates and cancels deliveries; sends status webhooks | external system |
| Worker | publishes the outbox, consumes queues, reconciles, retries | internal |

## 5. High-level flow (happy path)

```
Customer                     FulfillmentHub (API)              Worker                    Providers (simulated)
  |  POST /orders (Idempotency-Key)  |                            |                              |
  |--------------------------------->| validates, reserves stock, |                              |
  |                                  | saves Order(Created) +     |                              |
  |  201 Created (order)             | Outbox(OrderPlaced)        |                              |
  |<---------------------------------|                            |                              |
  |                                  |                            | reads outbox → CreatePayment |
  |                                  |                            |----------------------------->| Payment provider
  |                                  |                            |  payment pending             |
  |                                  |  Order → AwaitingPayment   |<-----------------------------|
  |                                  |                            |                              |
  |                                  |  POST /webhooks/payments   |                              | (webhook: paid)
  |                                  |<--------------------------------------------------------- |
  |                                  | dedup (event id), stores   |                              |
  |                                  | WebhookEvent, enqueues     |                              |
  |                                  |                            | processes → Payment Paid,    |
  |                                  |                            | Order → Paid, Outbox(OrderPaid)|
  |                                  |                            | → RequestDelivery:           |
  |                                  |                            |   POST delivery_quotes ----->| Delivery provider
  |                                  |                            |   POST deliveries (idem key)>|
  |                                  |  Order → DeliveryRequested |                              |
  |                                  |                            |                              |
  |                                  |  POST /webhooks/deliveries |                              | (status: pickup,
  |                                  |<--------------------------------------------------------- |  dropoff, delivered)
  |                                  |                            | applies event (ordered by    |
  |                                  |                            | status/timestamp), Order →   |
  |                                  |                            | InDelivery → Delivered       |
  |  GET /orders/{id}                |                            |                              |
  |--------------------------------->| current status + history   |                              |
```

## 6. Features by module

### Orders
- Create an order with items (product, quantity), a delivery address (snapshot) and an idempotency key.
- Validate: active customer, active products, quantities > 0, stock available (reservation with optimistic concurrency).
- Pricing: unit price frozen on the item (snapshot), subtotal, delivery fee (from the quote), total.
- Status with a transition history (who / when / why).
- Cancellation by the customer (while unpaid or before pickup, per the rules) and by the operator; releases stock;
  requests a refund or a delivery cancellation when applicable.
- Transaction: order + stock reservation + delivery quote + the `OrderPlaced` event in the outbox, in the same commit
  (Phase 8). **Eventual consistency**: `POST /orders` answers `Created`; the Worker creates the payment right after
  (normally under a second) and the order moves to `AwaitingPayment` → `Paid` → `DeliveryRequested` on its own — the
  customer follows it through `GET /orders/{id}`.

### Payments
- Simulated provider (no real money). Lifecycle: `Pending → Authorized → Paid` | `Failed` | `Cancelled` | `Refunded`.
- Asynchronous creation through the outbox (`OrderPlaced` event); attempts recorded as `PaymentAttempt`.
- Provider webhook with an `event_id` (deduplication), HMAC signature, possibly duplicated or delayed.
- Reconciliation: a periodic job queries the provider for payments that have been pending for too long.
- Transient provider failure → retry with backoff; permanent failure → order cancelled, stock released.

### Deliveries
- Quote (`DeliveryQuote`) with an expiry (`expires`); requote when it expired before the delivery is created.
- Creation with an `idempotency_key`; tracked through webhooks and through reconciliation reads (`GET`).
- Statuses mirrored from the provider and mapped to the domain; event history with `provider_event_id`,
  `occurred_at` and `received_at`.
- Cancellation (honouring the provider's `noncancelable_delivery`).
- Out-of-order and duplicate events handled by rule (status + timestamp), never by arrival order.

### Catalog
- Products with SKU, price, stock, active/inactive. Simple CRUD (Admin). Concurrency scenario: stock.

### Customers
- Customer profile (name, e-mail, phone) and saved addresses. Linked to a `User` with the `Customer` role.

### Identity
- Users, roles (`Customer`, `Operator`, `Admin`), JWT login, authorization policies. No external OAuth (out of scope).

### Operations / Admin
- View of orders, payments, deliveries, webhook events, outbox (pending/failed), dead-lettered messages,
  attempts/retries, integration status (circuit open? slow provider?), relevant logs by correlation id.
- Actions: reprocess a failed outbox message or webhook, cancel a delivery, force a reconciliation.
- Technology: **Blazor** (Web App, interactive server rendering), Phase 17. Function over polish.

## 7. Out of scope (explicitly)

- Real integration with Uber, PSPs or any commercial supplier.
- Microservices, Kubernetes, Kafka, service mesh.
- A rich end-customer frontend (API plus an operational Admin only).
- Multi-tenancy, real multi-currency (currency is a field, but only BRL is used), tax/invoicing, marketplace.
- Real commercial production: the environments are local and a learning "dev/cloud".

## 8. Non-functional requirements (measurable targets for the portfolio)

| Requirement | Target | How it is proven |
|---|---|---|
| Idempotency | 0 duplicates on repeated `POST /orders`, duplicate webhooks and retried delivery creation | integration tests + database constraints |
| Consistency | no event lost between commit and publication | outbox + fault-injection tests |
| Resilience | a provider failing/slow 30% of the time still takes 100% of orders to a final state (or a controlled cancellation) | scenario with the simulator configured |
| Concurrency | 50 concurrent requests for the same product never leave stock negative | race-condition test |
| Observability | one `correlation id` follows an order end to end (API → queue → worker → provider) | trace + logs + runbook |
| Security | OWASP Top 10 reviewed; no secrets in code; SAST and dependency scanning in CI | SECURITY.md + CI |
| Operations | an operator can see and reprocess failures without touching the database | Admin UI |
| Deploy | `terraform apply` + pipeline bring up the dev environment on AWS; `terraform destroy` tears it down | DEPLOYMENT.md |

## 9. Glossary (ubiquitous language)

| Term | Meaning |
|---|---|
| Order | main aggregate; groups items, address, payment and delivery |
| Order Item | order line with a product and price snapshot |
| Payment | aggregate of the payments module; one order → one active payment; attempts in `PaymentAttempt` |
| Delivery Quote | a delivery offer from the provider with fee, ETA and expiry |
| Delivery | aggregate mirroring the provider's delivery and its event history |
| Webhook Event | event received from a provider, stored raw, deduplicated by `provider_event_id` |
| Outbox Message | domain event stored in the same commit as the state change and published later |
| Idempotency Record | record of an idempotent request (key + hash + response) |
| Provider | external (simulated) payment or delivery system |
| Simulator | .NET process that imitates a provider, with configurable failures |
| DLQ | dead-letter queue: messages that exhausted their attempts |
