# TEST_STRATEGY — FulfillmentHub

Goal: tests that **prove behaviour** and document the system's guarantees (idempotency, consistency, concurrency,
resilience). There is no target number of tests or numeric coverage goal; there is a **matrix of mandatory scenarios**.

## 1. Stack (ADR-012)

| Role | Choice | Rationale |
|---|---|---|
| Framework | **xUnit v3** | de facto standard in .NET; v3 runs on the Microsoft Testing Platform and supports `TestContext.Current.CancellationToken` |
| Assertions | **Shouldly** | readable, BSD licence; FluentAssertions v8 moved to a commercial licence for non-OSS use — avoid the bad signal |
| Test doubles | hand-written fakes on the ports (`IDeliveryProviderClient`, `IPaymentGatewayClient`, `IMessagePublisher`); `NSubstitute` only if a manual fake becomes expensive | explicit fakes are more readable and do not couple to the implementation |
| Time | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) | quote expiry, backoff and reconciliation without `Thread.Sleep` |
| External HTTP | fake `HttpMessageHandler` for the policies; **in-process** simulator (`WebApplicationFactory` of the `ProviderSimulator`) for flows | tests the retry matrix without the network and the real contract with it |
| Database | **Testcontainers** PostgreSQL 17 (one container per class/collection, clean database per test via `Respawn` or one schema per test) | the in-memory provider hides transaction/constraint/SQL bugs |
| Queue | Testcontainers **LocalStack** (SQS) | same API as real SQS |
| Host | `WebApplicationFactory<Program>` with configuration/service overrides | tests the full pipeline (auth, filters, ProblemDetails) |
| Architecture | **NetArchTest.Rules** | enforces dependency direction and conventions |
| E2E | `docker compose` + xUnit tests calling the real API (`E2E` category, outside the default run) | proves the assembled system |
| Load | k6 **or** NBomber (to be decided in Phase 18) | |

## 2. Pyramid (what each layer proves)

### Unit (fast, no I/O) — `FulfillmentHub.UnitTests`
- Domain: invariants, state machines (valid, invalid and idempotent transitions), `Money`, the canonical ordering rule for delivery events, total calculation.
- Application: use cases with fakes of the ports and **a real DbContext on SQLite? No** — use cases that depend on EF are tested at the integration level (avoid doubles of `DbSet`).
- Policies: retry predicate (which statuses), backoff + jitter calculation (seeded), provider error mapping → `Result`.
- Mappings: DTO ↔ domain, webhook payload → command.

### Integration — `FulfillmentHub.IntegrationTests`
- API + real database: every endpoint (happy path + relevant failures), ProblemDetails, authorization, idempotency, constraints.
- Persistence: EF mappings, migrations on a clean database, concurrency (`xmin`), `SKIP LOCKED`.
- Outbox/worker: zero loss, retry, `Failed`; SQS consumers with LocalStack.
- Integration with the in-process simulator (real HTTP client → simulator hosted in another `WebApplicationFactory`).

### Architecture — `FulfillmentHub.ArchitectureTests`
- `Domain` does not reference `Application`/`Infrastructure`/framework packages; `Application` does not reference `Infrastructure`; hosts do not reference `Domain` directly for logic (they may for types).
- Conventions: `sealed` use cases, `Handler` suffix, endpoints in `*Endpoints` classes, no public classes in `Infrastructure` outside the registration extension methods (to be reviewed).

### E2E — `FulfillmentHub.E2ETests` (Phase 12)
- Few flows: (1) order → paid → delivered; (2) order → payment declined → cancelled with stock released; (3) order → delivery cancelled by the provider → refund. With the simulator in a moderately chaotic mode.

### Contract (Phase 12)
- Client DTOs vs. simulator responses (and vs. the reference OpenAPI spec for the reproduced fields).

## 3. Matrix of mandatory scenarios

| # | Guarantee | Test scenario | Layer | Phase |
|---|---|---|---|---|
| T1 | API idempotency | same `Idempotency-Key` + same body → 1 order, same response (201 → 201 with the same id) | integration | 4 ✔ `PlaceOrder_RepeatedWithSameKeyAndPayload_ReplaysResponse_WithoutSecondOrder` |
| T2 | API idempotency | same key + different body → 422 | integration | 4 ✔ `PlaceOrder_RepeatedWithSameKeyButDifferentPayload_Returns422` |
| T3 | API idempotency | two concurrent requests with the same key → one 201, the other 409 (or the same 201 after completion) | integration | 4 ✔ `PlaceOrder_ConcurrentRequestsWithSameKey_CreateExactlyOneOrder` (10 in parallel) |
| T4 | Stock concurrency | 20 parallel tasks buying the last unit → exactly 1 success; stock = 0; **the test fails if the concurrency token is removed** | integration | 4 ✔ `PlaceOrder_TwentyBuyersForTheLastUnit_ExactlyOneSucceeds`; verified on 2026-09-18: without `UseXminAsConcurrencyToken` on `Product` the test failed in 3/3 runs ("there is only one unit in stock") |
| T5 | State machine | every transition in the table (valid) and one invalid transition per state | unit | 2 ✔ `OrderTests`, `PaymentTests`, `DeliveryTests` |
| T6 | Duplicate webhook | same `event id` twice → 200 both times, a single effect, duplicate metric | integration | 5/7 ✔ `DuplicateWebhook_IsAcknowledged_ButAppliedOnce` (1 row in `webhook_events`, 1 `Paid` transition) |
| T7 | Out-of-order webhook | `delivered` arrives before `pickup` → state goes to `Delivered` and the later `pickup` is recorded as `Stale` (not applied) | integration | 7 ✔ `Webhooks_OutOfOrder_NeverRegressTheDelivery_ButAreRecorded` (+ duplicate of the same `id` → 1 row, 1 transition); end to end `Order_IsDelivered_ByTheProvidersWebhooks_EndToEnd`; `ReturnedParcel_CancelsTheOrder_AndReleasesStock`; `LostWebhooks_AreRecoveredByReconciliation`; wrong signature / the other provider's header / old timestamp → 401 |
| T8 | Webhook signature | invalid signature/old timestamp → 401, nothing persisted | integration | 5 ✔ `Webhook_WithBadSignature_IsRejected_AndNothingIsPersisted` (wrong key, timestamp −10 min, missing header, altered body); `…MalformedPayload_Returns400`; `Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted` (D-P5) |
| T9 | Correct retry | 429 with `Retry-After` → waits and repeats; contract 4xx → no retry; 503 ×4 → `Unavailable` after 3 retries; per-attempt timeout → retries | unit (fake handler) | 5 ✔ `PaymentGatewayClientTests` (real resilience pipeline against a `ScriptedTransport`) |
| T10 | Circuit breaker | opens after N failures; subsequent calls fail fast (`BrokenCircuitException`); closes after a successful half-open probe | unit/integration | 5/6 ✔ `CircuitBreaker_Opens_AfterSustainedFailures_AndFailsFastWithoutCallingTheProvider` (payment) + `CircuitBreaker_Opens_AfterSustainedFailures_AndClosesAgain_AfterASuccessfulProbe` (delivery: opens after 10 failures, the half-open probe after 1 s closes the circuit) |
| T11 | Expired quote | `expires` in the past → requote; 2nd expiry → business failure | integration | 6 ✔ `RequestDelivery_WhenTheCheckoutQuoteExpired_RequotesOnce` (1-second sandbox quote) and `RequestDelivery_WhenTheQuoteExpiresTwice_GivesUp_ForAnOperator` (provider forced to `expired_quote` → `delivery.quote_expired`, order stays `Paid`) |
| T12 | Outbox zero loss | exception injected after `SaveChanges` and before publish → the message stays in the outbox and is published by the worker | integration | 8 ✔ `OrderPlaced_IsCommittedWithTheOrder_AndPublishedByTheWorker` (row written in the same commit, no effect during the request) + `PublishFailure_AfterTheCommit_IsRetried_UntilTheHandlerSucceeds` (provider down on the 1st publish → `Pending`, `attempts=1`, success later) |
| T13 | Outbox retry/Failed | handler fails 3× → `attempts=3`, `Failed`; requeue through the endpoint → success | integration | 8 ✔ `HandlerFailingRepeatedly_ParksTheMessageAsFailed_AndAnAdministratorCanRequeueIt`; `RedeliveringAProcessedMessage_HasNoSecondEffect` (idempotent consumer); `CancellingAPaidOrder_RefundsThePayment_ThroughTheOutbox` |
| T14 | Idempotent consumer | same SQS message delivered twice → one effect | integration (LocalStack) | 9 ✔ `RedeliveredMessage_IsAcknowledged_WithoutASecondEffect` (1 payment, 1 row in `processed_messages`) |
| T15 | DLQ | poison message → DLQ after `maxReceiveCount` | integration (LocalStack) | 9 ✔ `PoisonMessage_LandsInTheDeadLetterQueue_AfterMaxReceiveCount` (3 receives with backoff, then in the DLQ); `Order_IsPaidAndShipped_ThroughTheQueues` (flow through both queues) |
| T16 | Authorization | customer A `GET /orders/{B's id}` → 404 (existence not leaked); operator → 200 | integration | 4 ✔ `Customer_CannotSeeOrCancel_AnotherCustomersOrder`, `Operator_SeesEveryOrder_AndCancelsWithOperatorAction` |
| T17 | Permanent payment failure | `card_declined` → `Payment Failed`, `Order Cancelled(PaymentFailed)`, stock released (`OrderCancelled` outbox event in Phase 8) | integration | 5 ✔ `DeclinedPayment_CancelsOrder_AndReleasesStock` (real webhook from the simulator, sandbox amount `…99`) |
| T18 | Reconciliation | payment `Pending` for > X → the job queries the provider (`paid`) → state corrected | integration | 5 ✔ `SilentSettlement_IsPickedUpByReconciliation` (lost webhook, amount `…98`); `ProviderOutage_NeverFailsTheOrder_AndReconciliationRetriesTheCreation` (503 → order `Created`, reconciliation recreates at the provider); `PaymentCapturedAfterCustomerCancelled_…` (late capture does not resurrect a cancelled order) |
| T19 | End-to-end trace | one order produces API→outbox→worker→provider spans with the same `trace_id` | integration (in-memory exporter) | 11 |
| T20 | Convergence under chaos | `SIM_FAILURE_RATE=0.3`, `SIM_WEBHOOK_OUT_OF_ORDER=true` → 100% of orders reach a final state within ≤ N s | E2E | 12 |

## 4. Test quality rules

- Naming: `Method_Scenario_ExpectedResult` or a sentence (`PlaceOrder_WhenStockInsufficient_ReturnsConflict`). AAA with clear sections.
- One behaviour per test; assert on **observable output** (HTTP response, database rows, published messages, aggregate state), not on internal calls.
- Forbidden: tests that only verify a method was called; tests that copy the implementation; `Thread.Sleep`; ordering dependencies between tests; shared mutable data; `DateTime.Now`.
- Builders/`ObjectMother` for data (`AnOrder().WithItems(...).Build()`), no giant fixtures.
- Integration: clean database per test (Respawn) or rolled-back transaction; containers shared per collection (`ICollectionFixture`).
- Integration tests run in the default `dotnet test` (Docker required); E2E and load tests sit behind a category/trait.
- A failing test in CI is blocking. Flaky = bug: fix or remove, never an automatic `retry`.

## 5. How to run (from Phase 1 on)

```bash
dotnet test                                   # unit + architecture + integration (Docker required)
dotnet test --filter "Category!=E2E"          # without E2E
dotnet test tests/FulfillmentHub.E2ETests     # after docker compose up
```

## 6. Evidence for the portfolio
Every guarantee in the matrix will have a link in the final README to the corresponding test ("it is here, I implemented it").
