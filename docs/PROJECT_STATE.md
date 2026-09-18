# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 6 — Fase 5)

## Fase atual
**Fase 5 — Payments: CONCLUÍDA (Gate 5 fechado).**
Próxima fase: **Fase 6 — Delivery provider simulator + integração de saída** (não iniciada).

## Concluído (Fase 5)
- ProviderSimulator `/payments/v1` (`Program` nomeado em `FulfillmentHub.ProviderSimulator`, hospedável por `WebApplicationFactory<ProviderSimulator.Program>`): `POST /payments` (Bearer `Simulator:Payments:ApiKey`; `Idempotency-Key` obrigatório → replay 200 / 409 `idempotency_conflict`), `GET /payments/{id}`, `POST /payments/{id}/refunds`; `PaymentSimulatorStore` em memória (D-43); `PaymentSettlementService` liquida após `SettleDelayMs` e enfileira `payment.status_changed`; `WebhookDispatcher` assina (`X-Signature` HMAC-SHA256 hex, `X-Timestamp`, `X-Event-Id`) e repete 1/2/4 s; `ChaosFilter` (`Simulator:Chaos`); regras sandbox por valor: centavos `…99` recusa, `…98` aprova sem webhook (D-46); erros `{code,message,kind:"error"}`.
- Application/Payments: `IPaymentGatewayClient` (`CreateAsync/GetAsync/RefundAsync`, DTOs próprios), `CreatePaymentForOrderHandler` (in-process após `PlaceOrder`, D-44; fecha tentativas abandonadas, D-45; `Unavailable` → `TransientFailure` sem falhar o pedido; permanente → `Failed` + cancela + libera estoque), `ApplyPaymentWebhookHandler` (D-P5 = sim: `paid`/`refunded` confirmados com `GET`, D-42), `PaymentStatusApplier` (único ponto que aplica status ao `Payment`/`Order`; captura tardia após cancelamento não ressuscita o pedido, D-47; idempotente para pedidos já adiante), `ReconcilePaymentsHandler` (pendentes há > X: sem id no provider → recria; com id → relê e aplica; falha por item isolada), `PaymentsMetrics` (`fh.webhooks.received/rejected/duplicates`, `fh.reconciliation.corrections`, `fh.payments.settled{status}`), `Failure.Unavailable`, `AnonymousCurrentUser` (Worker). `AddFulfillmentHubApplication()` separado de `AddFulfillmentHubIdentityApplication()` (D-48).
- Infrastructure: `Providers/Payments` — `PaymentProviderOptions` (`Providers:Payment:*`, validadas no start), `SimulatedPaymentGatewayClient` (snake_case, spans `Provider CreatePayment/GetPayment/RefundPayment`, mapeamento de erros → `Failure`), `AddFulfillmentHubPaymentProvider()` com `Microsoft.Extensions.Http.Resilience` (timeout total → retry exp+jitter com `Retry-After` só em 408/429/5xx/transporte/timeout → CB 50 %/30 s/mín 10 → timeout por tentativa; `MaxRetryAttempts=0` desliga o retry). `Webhooks/` — `WebhookEvent` (tabela `webhook_events`, `UNIQUE(provider, provider_event_id)`, payload jsonb, xmin), `WebhookInbox` (escopo DI próprio; `SELECT` antes do `INSERT`, D-50), `WebhookSignatureVerifier` (tempo constante + tolerância 300 s). Migration `20260918143818_WebhookEvents` aplicada no banco local.
- Api: `POST /api/v1/webhooks/payments` (anônimo, rate limit `webhooks` 120/min, corpo ≤ 64 KB → 413, assinatura inválida → 401 ProblemDetails, malformado → 400, duplicado → 200 no-op, sempre 200 após persistir; eventos 5200–5202 sem corpo/chave); `PlaceOrder` chama `CreatePaymentForOrderHandler` e devolve o pedido já `AwaitingPayment`; `GlobalExceptionHandler` mapeia `BadHttpRequestException` → 4xx (D-49, achado no smoke: JSON ilegível dava 500 em Development). `appsettings.json` com `Providers:Payment` (chaves vazias).
- Worker: `AddFulfillmentHubPaymentProvider()` + `AddFulfillmentHubApplication()`; `ReconciliationOptions` (`Worker:Reconciliation`: `IntervalSeconds` 60, `PendingForSeconds` 120, `BatchSize` 50); `PaymentReconciliationService` (PeriodicTimer + escopo por execução, logs 2100–2102).
- Testes: **185 no total** (109 unit, 5 architecture, 71 integration). Novos: `PaymentGatewayClientTests` (10: header/bearer/mapeamento; 429 `Retry-After` honrado; 4xx sem retry ×5; 503×4 → `Unavailable`; timeout por tentativa repetido; CB abre após 10 falhas e falha rápido sem rede) — T9/T10; `PaymentFlowTests` (10: pedido → `AwaitingPayment` → `Paid` via webhook real; T17 recusa → `Cancelled(PaymentFailed)` + estoque devolvido; T18 liquidação silenciosa corrigida pela reconciliação; outage 503 → pedido `Created` sem `PaymentId`, reconciliação recria e o mesmo `Payment` conclui; captura tardia após cancelamento; T6 duplicado → 1 linha, 1 transição; D-P5 webhook "paid" para pagamento recusado não confirma; T8 chave errada/timestamp velho/sem header/corpo alterado → 401 e nada persistido; malformado → 400; pagamento desconhecido → 200 `Ignored`); `PaymentSimulatorContractTests` (4: replay/409, sem chave → 400, 401, 404); `PlaceOrder_WithUnreadableBody_Returns400Problem_NotA500`. Fixture: `ApiFixture` hospeda o simulator in-process (`ProviderSimulatorFactory`), o `HttpClient` da API aponta para o `TestServer` do simulator e o dispatcher de webhooks do simulator aponta para o `TestServer` da API; `ProviderOutage` liga/desliga 503; `RouteHandlerOptions.ThrowOnBadRequest=true` como em Development.
- Verificação manual em Kestrel (API 5000 + simulator 5100 + worker): `POST /orders` → 201 `AwaitingPayment` → `Paid` em ~4 s pelo webhook assinado; JSON inválido → 400; webhook forjado → 401; worker reconciliou sem erros; 0 chaves/tokens nos logs. Build 0 warnings; `dotnet format` limpo; sem pacotes vulneráveis. User-secrets `Providers:Payment:ApiKey`/`WebhookSigningKey` definidos para Api e Worker (valores dev-only iguais ao `appsettings.Development.json` do simulator).

## Em andamento
- Nada.

## Próximas tarefas (Fase 6 — Delivery; ler antes `fulfillmenthub-dotnet` §9/§11, `httpclient-factory`, `resilience`, INTEGRATIONS.md §1–§2 e `.ai/reference/uber-direct-*openapi.yaml`, ADR-003, D-P3)
1. Simulator `/delivery/v1` (mesmo padrão do pagamento: `Simulator:Delivery:*`, `ChaosFilter`, `WebhookDispatcher` reutilizado): `POST /auth/token` (client credentials fake), `POST /customers/{id}/delivery_quotes`, `POST /customers/{id}/deliveries` (`quote_id`, `idempotency_key`/`external_id` → `409 duplicate_delivery`), `GET /deliveries/{id}`, `POST /deliveries/{id}/cancel`; ciclo `pending → pickup → pickup_complete → dropoff → delivered` temporizado (`StepMs`), `canceled`/`returned`; erros de INTEGRATIONS §1.4 (`expired_quote`, `couriers_busy` 503, `address_undeliverable` 400, `invalid_token` 401…); cenários por CEP/valor como no pagamento.
2. Application/Deliveries: `IDeliveryProviderClient` (`QuoteAsync`, `CreateAsync`, `GetAsync`, `CancelAsync`), `RequestDeliveryHandler` (dispara após `Paid` — in-process nesta fase, via outbox na Fase 8): cota → cria → `Order.MarkDeliveryRequested(deliveryId, fee)` (resolver D-P3: taxa cotada no momento do pedido pago); recotação em `expired_quote` (1×); `409 duplicate_delivery` → `GET` e adota.
3. Infrastructure/Providers/Deliveries: typed client com token cache (renovação antecipada; 401 → renovar 1× e repetir), pipeline igual à do pagamento (extrair `AddFulfillmentHubResiliencePipeline` comum só se a duplicação incomodar), Options `Providers:Delivery:*`.
4. Testes: matriz de retry do delivery client (reaproveitar `ScriptedTransport` → mover para `tests/.../Support`), CB half-open (fecha após sucesso) — completa T10; T11 cotação expirada com `FakeTimeProvider`; fluxo pago → `DeliveryRequested` com simulator in-process (webhooks de entrega só na Fase 7).
5. Docs: INTEGRATIONS §2 (contrato final + chaves `Simulator:Delivery:*`), DOMAIN §12, DECISIONS (D-P3), BACKLOG (BL-062…065), ROADMAP; fechar Gate 6.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-42…D-50)
- D-42 D-P5 = sim (verificar `paid`/`refunded` no provider); D-43 D-P8 = memória; D-44 pagamento in-process após `PlaceOrder`, provider nunca falha o pedido; D-45 tentativas abandonadas; D-46 valores sandbox; D-47 captura tardia não ressuscita pedido cancelado (estorno automático → BL-244); D-48 registro de identidade separado do restante da Application; D-49 `BadHttpRequestException` → 4xx; D-50 inbox consulta antes de inserir.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P3 cotação síncrona vs. taxa estimada (Fase 6) · D-P4 k6/NBomber · D-P6 rede AWS dev · D-P7 estado Terraform.

## Testes atuais
- 185 testes verdes (~30 s a quente; suíte de pagamento usa webhooks reais com `SettleDelayMs=100`). Matriz TEST_STRATEGY: T1–T6 ✔, T8 ✔, T9 ✔, T10 parcial (half-open na Fase 6), T16–T18 ✔.

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco com 4 migrations (`InitialCreate`, `DomainModel`, `IdempotencyRecords`, `WebhookEvents`) e seed; user-secrets da Api: `Database:ConnectionString`, `Jwt:SigningKey`, `Seed:*Password`, `Providers:Payment:ApiKey`, `Providers:Payment:WebhookSigningKey`; do Worker: `Database:ConnectionString`, `Providers:Payment:*`. Nenhum remote/GitHub/AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 5 — fechado.** Critérios: fluxo pedido→pago em integração com o simulator in-process ✔ (`PlaceOrder_InitiatesPayment_AndWebhookMarksOrderPaid`); webhook duplicado sem efeito duplo ✔ (T6); assinatura inválida → 401 ✔ (T8); falha permanente cancela pedido e libera estoque ✔ (T17); reconciliação corrige pendente ✔ (T18 + outage); matriz de retry testada ✔ (T9/T10); BACKLOG P0 de Payments fechado ✔ (BL-027, 060, 061, 104).
**Gate 6 — Delivery**: matriz de retry do delivery client coberta; circuit breaker abre **e fecha** (half-open) em teste; cotação expirada recotada (T11); `409 duplicate_delivery` reconciliado; disclaimer presente no README do simulator (já está); fluxo `Paid → DeliveryRequested` em integração com o simulator in-process.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 6), com `docker compose --profile deps up -d` ativo.
