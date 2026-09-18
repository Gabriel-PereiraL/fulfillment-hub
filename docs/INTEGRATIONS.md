# INTEGRATIONS — providers externos (simulados)

> **This project does not connect to Uber infrastructure or to any real payment provider.**
> **The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.**
> Nenhuma credencial real, nenhuma entrega real, nenhuma cobrança real. Nada aqui deve ser lido como integração
> profissional ou comercial com a Uber ou qualquer fornecedor.

## 1. Provider de entrega: contrato de referência (Uber Direct API)

### 1.1 Fontes consultadas

| Fonte | URL | Consultado em | Observação |
|---|---|---|---|
| Uber Direct — visão geral | https://developer.uber.com/docs/deliveries/overview | 2026-09-18 | portal é SPA; conteúdo técnico vem das páginas abaixo |
| Uber Direct — Get started (auth, fluxo) | https://developer.uber.com/docs/deliveries/get-started | 2026-09-18 | token, `customer_id`, passos |
| Delivery Status Webhook (DaaS) | https://developer.uber.com/docs/deliveries/daas/references/api/webhooks/delivery-status-webhook | 2026-09-18 | `event.delivery_status`, payload, statuses |
| Webhooks — segurança/retry (guia geral Uber) | https://developer.uber.com/docs/riders/guides/webhooks | 2026-09-18 | HMAC-SHA256 hex, retry/backoff, sem ordem garantida |
| SDK oficial `uber/uber-direct-sdk` (Apache-2.0) — `src/deliveries/openapi.yaml` (Direct API v1.0.1) e `src/auth/openapi.yaml` | https://github.com/uber/uber-direct-sdk | 2026-09-18 (último commit do repo: 2024-10-24) | cópia privada em `.ai/reference/` para consulta; spec OpenAPI 3.1 com schemas/erros |

A referência primária para schemas e códigos de erro é a spec OpenAPI do SDK oficial (mantido pela Uber). Onde o portal e a spec
divergirem, prevalece o portal (mais recente) e a divergência deve ser anotada aqui.

### 1.2 Autenticação (real)

- `POST https://auth.uber.com/oauth/v2/token` (portal) / `https://login.uber.com/oauth/v2/token` (spec do SDK) — `application/x-www-form-urlencoded`:
  `client_id`, `client_secret`, `grant_type=client_credentials`, `scope=eats.deliveries`.
- Resposta: `access_token`, `token_type=Bearer`, `expires_in` (portal: 2 592 000 s = 30 dias), `scope`.
- Todas as chamadas: `Authorization: Bearer <token>`; base `https://api.uber.com/v1`; `customer_id` no path.
- Sandbox: credenciais em "Test mode" no dashboard não geram entregas reais.

### 1.3 Endpoints (real) — Direct API

| Operação | Método e path | Request (principais campos) | Response (principais campos) |
|---|---|---|---|
| Create Quote | `POST /customers/{customer_id}/delivery_quotes` | **`pickup_address`**, **`dropoff_address`** (strings JSON estruturadas), `pickup_latitude/longitude`, `dropoff_latitude/longitude`, `pickup_ready_dt`, `pickup_deadline_dt`, `dropoff_ready_dt`, `dropoff_deadline_dt` (RFC 3339), `pickup_phone_number`, `dropoff_phone_number`, `manifest_total_value` (centavos), `external_store_id` | `kind=delivery_quote`, `id` (`dqt_…`), `created`, **`expires`**, `fee` (centavos), `currency` (`brl`), `currency_type` (`BRL`), `dropoff_eta`, `duration` (min), `pickup_duration` (min), `dropoff_deadline` |
| Create Delivery | `POST /customers/{customer_id}/deliveries` | **`pickup_name`, `pickup_address`, `pickup_phone_number`, `dropoff_name`, `dropoff_address`, `dropoff_phone_number`, `manifest_items[]`** (`name`, `quantity`, `size` `small|medium|large|xlarge`, `dimensions`, `price`, `weight`, `must_be_upright`), `quote_id`, `manifest_reference`, `manifest_total_value`, `pickup_notes`, `dropoff_notes`, `deliverable_action`, `undeliverable_action`, `requires_dropoff_signature`, `requires_id`, `tip`, **`idempotency_key`** ("persists for a set time frame, defaulting to 60 minutes"), `external_id`, `external_store_id`, `*_dt` janelas | `id` (`del_…`), `quote_id`, **`status`** (`pending`, `pickup`, `pickup_complete`, `dropoff`, `delivered`, `canceled`, `returned`), `complete`, `courier` (`name`, `rating`, `vehicle_type`, `phone_number`, `location{lat,lng}`, `img_href`), `courier_imminent`, `created`, `updated`, `currency`, `fee`, `tracking_url`, `pickup`/`dropoff` (waypoints), `pickup_eta`, `dropoff_eta`, `pickup_ready/deadline`, `dropoff_ready/deadline`, `manifest`, `manifest_items`, `external_id`, `live_mode`, `undeliverable_action`, `undeliverable_reason`, `related_deliveries`, `uuid`, `batch_id` |
| Get Delivery | `GET /customers/{customer_id}/deliveries/{delivery_id}` | — | mesmo shape de `DeliveryResp` |
| List Deliveries | `GET /customers/{customer_id}/deliveries` | filtros/paginação | lista |
| Update Delivery | `POST /customers/{customer_id}/deliveries/{delivery_id}` | `dropoff_notes`, `pickup_notes`, `manifest_reference`, `tip_by_customer`, verificações, `dropoff_latitude/longitude` | `DeliveryResp` |
| Cancel Delivery | `POST /customers/{customer_id}/deliveries/{delivery_id}/cancel` | — | entrega cancelada (`status=canceled`) |
| Proof of Delivery | `POST /customers/{customer_id}/deliveries/{delivery_id}/proof-of-delivery` | | imagem/dados |

### 1.4 Erros (real) — shape `{ "code": "...", "message": "...", "kind": "error", "metadata"?: {...} }`

| HTTP | `code` | Significado | Nossa reação |
|---|---|---|---|
| 400 | `invalid_params`, `address_undeliverable`, `unknown_location`, `pickup_window_too_small`, `dropoff_deadline_too_early`, `dropoff_deadline_before_pickup_deadline`, `dropoff_ready_after_pickup_deadline`, `pickup_ready_too_early`, `pickup_deadline_too_early`, `pickup_ready_too_late`, `address_undeliverable_limited_couriers`, `expired_quote`, `used_quote`, `mismatched_price_quote`, `not_allowed`, `noncancelable_delivery`, `max_tip_exceeded`… | erro de contrato/regra | **sem retry**; `expired_quote`/`used_quote` → recotar (máx. 2); `noncancelable_delivery` → falha de negócio |
| 401 | `unauthorized` | credencial inválida | sem retry; renovar token uma vez se expirado |
| 402 | `customer_suspended`, `missing_payment` | conta | sem retry; alerta operacional |
| 403 | `customer_blocked` | conta | sem retry; alerta |
| 404 | `customer_not_found`, `delivery_not_found` | recurso | sem retry (em `GET` pós-criação: 1 retry curto, pode ser eventual) |
| 408 | `request_timeout` | timeout no provider | retry (criação só com `idempotency_key`) |
| 409 | `duplicate_delivery` (`metadata.delivery_id`) | já existe entrega ativa igual | **sem retry**; `GET` no id retornado e reconciliar |
| 429 | `customer_limited` | limite de conta | retry respeitando `Retry-After` (se presente); métrica |
| 500 | `internal_server_error`, `unknown_error` | falha do provider | retry limitado (backoff + jitter) |
| 503 | `service_unavailable`, `couriers_busy`, `robo_couriers_busy` | indisponível / sem courier | retry com backoff maior; `couriers_busy` persistente → falha de negócio (`DeliveryFailed`) |

Rate limits numéricos **não estão documentados publicamente** nas fontes consultadas; o simulator aplica um limite configurável para exercitar o 429.

### 1.5 Webhook de status (real) — `event.delivery_status`

- Enviado ao `webhook_url` configurado no dashboard, `POST` JSON. Cabeçalhos de assinatura: **`X-Uber-Signature`** e/ou **`X-Postmates-Signature`**
  = HMAC-SHA256 **hexadecimal** do corpo bruto, chave = *signing key* do webhook (Uber Direct usa uma chave por webhook, distinta do client secret — fonte: guia de webhooks; verificar no dashboard).
- Payload (portal DaaS): `id` (event id), `kind=event.delivery_status`, `created`, `status`, `delivery_id`, `customer_id`, `account_id`, `developer_id`, `live_mode`, `batch_id`, `route_id`, `data{...}` = objeto completo da entrega (`id`, `status`, `courier`, `courier_imminent`, `pickup_eta`, `dropoff_eta`, `fee`, `tracking_url`, `complete`, `manifest`, `pickup`, `dropoff`, `return`…).
  (Há também o webhook legado `dapi.status_changed` com `event_id`, `event_time`, `meta.status` em MAIÚSCULAS — **não** reproduzido.)
- Statuses: `pending`, `pickup`, `pickup_complete`, `dropoff`, `delivered`, `canceled`, `returned` (+ `shopping_completed`, fora do escopo).
- Garantias: **pode ser enviado mais de uma vez e não há garantia de ordem**; deduplicar por `id`.
- Retry do provider: se a URL responde ≠ 2xx/inacessível → backoff exponencial (multiplicador 30 s), até 7 tentativas em ~1 h.
- Resposta esperada: `200` com corpo vazio, rapidamente.

## 2. Simulator de entrega: `FulfillmentHub.ProviderSimulator` (rotas `/delivery/v1/...`)

### 2.1 REAL PROVIDER CONTRACT vs LOCAL SIMULATOR

| Aspecto | Contrato real (Uber Direct) | Simulator local | Reproduzido? |
|---|---|---|---|
| Base URL | `https://api.uber.com/v1` | `http://localhost:5100/delivery/v1` | path/shape sim; host não |
| Auth | OAuth2 client credentials, `eats.deliveries`, Bearer | `POST /delivery/oauth/token` (client_credentials, credenciais fake configuráveis) → Bearer com `expires_in` curto para exercitar renovação | parcial (sem OAuth real) |
| `customer_id` no path | sim | sim (`cus_sim_…`) | sim |
| Create Quote | request/response acima | mesmos nomes de campo do subconjunto: `pickup_address`, `dropoff_address`, `*_dt`, `manifest_total_value` → `id dqt_`, `expires`, `fee`, `currency`, `dropoff_eta`, `duration`, `pickup_duration` | subconjunto |
| Create Delivery | request/response acima | subconjunto: campos obrigatórios + `quote_id`, `idempotency_key`, `external_id`, `manifest_items` (sem dimensões/verificações) → `id del_`, `status`, `tracking_url`, `fee`, `courier`, `*_eta` | subconjunto |
| Get / Cancel Delivery | sim | sim (`noncancelable_delivery` após `pickup_complete`) | sim |
| List / Update / Proof of Delivery | sim | **não** | não |
| Erros | tabela 1.4 | mesmos `code`/HTTP para o subconjunto: `invalid_params`, `address_undeliverable`, `expired_quote`, `used_quote`, `duplicate_delivery`(409+metadata), `customer_limited`(429+Retry-After), `internal_server_error`, `couriers_busy`, `service_unavailable`, `request_timeout`, `unauthorized`, `noncancelable_delivery`, `delivery_not_found` | subconjunto |
| `idempotency_key` | 60 min | 60 min (configurável) | sim |
| Webhook | `event.delivery_status`, HMAC hex em `X-Uber-Signature`/`X-Postmates-Signature`, sem ordem, com duplicatas, retry 7x | mesmo evento/shape do subconjunto (`id`, `kind`, `created`, `status`, `delivery_id`, `data{id,status,courier,courier_imminent,tracking_url,fee,pickup_eta,dropoff_eta,complete}`), mesma assinatura e headers, **cenários de duplicidade/atraso/fora de ordem configuráveis**, retry com backoff | subconjunto |
| Courier real, mapa, tracking page | sim | `tracking_url` fictícia; courier fake com localização aleatória | não |
| Sandbox oficial | sim | não usado | — |

### 2.2 Ciclo de vida simulado

`pending` → (após `SIM_DELIVERY_COURIER_ASSIGN_MS`) `pickup` → `pickup_complete` → `dropoff` → `delivered`; ramos: `canceled` (cancel antes de `pickup_complete` ou cenário), `returned` (cenário).
Cada transição gera um webhook. O simulator persiste estado em memória (ou SQLite/arquivo se precisar sobreviver a restart — decidir na Fase 6; padrão: memória).

### 2.3 Configuração de cenários (variáveis de ambiente / `appsettings`)

> Desenho original da Fase 0. A implementação (Fase 5) usa seções de configuração .NET em vez de variáveis `SIM_*`: o caos genérico ficou em `Simulator:Chaos:*` e o pagamento em `Simulator:Payments:*` (ver §3). As chaves de entrega serão definidas na Fase 6 seguindo o mesmo padrão (`Simulator:Delivery:*`).

| Variável | Efeito | Default |
|---|---|---|
| `SIM_LATENCY_MS` / `SIM_LATENCY_JITTER_MS` | latência artificial base/jitter em todas as rotas | 50 / 50 |
| `SIM_FAILURE_RATE` | fração (0–1) de respostas 500 aleatórias | 0 |
| `SIM_TIMEOUT_RATE` | fração de requisições que "somem" (nunca respondem até o timeout do cliente) | 0 |
| `SIM_RATE_LIMIT_PER_MINUTE` | acima disso → 429 + `Retry-After` | 0 (desligado) |
| `SIM_FORCE_STATUS` | força um código para a próxima N requisições (`503:3`) — rota admin do simulator | — |
| `SIM_COURIERS_BUSY_RATE` | fração de cotações/criações com 503 `couriers_busy` | 0 |
| `SIM_QUOTE_TTL_SECONDS` | validade da cotação (`expires`) | 900 |
| `SIM_DELIVERY_STEP_MS` | intervalo entre transições de status | 3000 |
| `SIM_WEBHOOK_DUPLICATE_RATE` | fração de webhooks enviados 2x | 0 |
| `SIM_WEBHOOK_OUT_OF_ORDER` | `true` embaralha a ordem de envio de eventos consecutivos | false |
| `SIM_WEBHOOK_DELAY_MS` | atraso no envio de webhooks | 0 |
| `SIM_WEBHOOK_FAIL_FIRST_N` | primeiras N entregas de webhook "falham" (força retry do simulator) | 0 |
| `SIM_WEBHOOK_SIGNING_KEY` | chave HMAC (dev only, não é secret real) | valor dev |
| `SIM_IDEMPOTENCY_TTL_MINUTES` | TTL de `idempotency_key` | 60 |

Rota administrativa do simulator (`POST /admin/scenario`) permite mudar o cenário em tempo de execução (testes E2E e demonstrações).

### 2.4 Implementação (Fase 6, 2026-09-18)

Rotas (`FulfillmentHub.ProviderSimulator/Deliveries`), com a mesma pipeline de caos das rotas de pagamento (`Simulator:Chaos`, 429 com código `customer_limited`):

| Operação | Método/path | Notas |
|---|---|---|
| Token | `POST /delivery/oauth/token` (form: `client_id`, `client_secret`, `grant_type=client_credentials`, `scope`) | `{ access_token, token_type: "Bearer", expires_in, scope }`; credenciais erradas → `401 { error: "invalid_client" }`; token opaco com `TokenLifetimeSeconds` (300 — curto de propósito, exercita a renovação) |
| Cotação | `POST /delivery/v1/customers/{customer_id}/delivery_quotes` | `pickup_address`/`dropoff_address` são **strings JSON** de `{ street_address[], city, state, zip_code, country }` (como no contrato real); resposta `{ kind: "delivery_quote", id: "dqt_…", created, expires, fee, currency: "brl", currency_type: "BRL", dropoff_eta, duration, pickup_duration, dropoff_deadline }` |
| Criar entrega | `POST …/deliveries` | `quote_id`, `idempotency_key`, `external_id`, `manifest_items[]`, nomes/telefones/endereços; resposta `{ kind: "delivery", id: "del_…", quote_id, status, complete, courier, courier_imminent, created, updated, currency, fee, tracking_url, pickup_eta, dropoff_eta, external_id, manifest_reference, live_mode: false, uuid, undeliverable_reason }` |
| Consultar / cancelar | `GET …/deliveries/{id}`, `POST …/deliveries/{id}/cancel` | cancelar só em `pending`/`pickup`; depois → `400 noncancelable_delivery` |
| Erros | `{ code, message, kind: "error", metadata? }` | `400 invalid_params / address_undeliverable / expired_quote / used_quote / noncancelable_delivery`, `401 unauthorized`, `404 customer_not_found / delivery_not_found`, `409 duplicate_delivery` (+ `metadata.delivery_id`; mesma `idempotency_key` dentro do TTL **ou** `external_id` com entrega ativa), `429 customer_limited` (+ `Retry-After`), `503 couriers_busy` (`CouriersBusyRate`), `500` (caos) |

Ciclo de vida: `pending` → (`CourierAssignMs`) `pickup` (courier fictício atribuído) → (`StepMs`) `pickup_complete` → `dropoff` → `delivered`; cada transição gera um `event.delivery_status` (`{ id, kind, created, status, delivery_id, customer_id, live_mode, data: <entrega> }`) assinado em **`X-Uber-Signature`** (HMAC-SHA256 hex) + `X-Timestamp` para `Simulator:Delivery:WebhookUrl` (Development: `http://localhost:5000/api/v1/webhooks/deliveries`, desde a Fase 7); `WebhookDuplicateRate`, `WebhookDelayMs` e `WebhookOutOfOrder` (atrasos aleatórios entre eventos consecutivos) reproduzem as garantias fracas do provider real.

**Regras de sandbox pelo CEP de entrega** (`zip_code`): começa com `00000` → `address_undeliverable` (cotação e criação); últimos dígitos `001` → cotação com validade de **1 s** (força `expired_quote`/recotação); `002` → a entrega termina em `returned`; `003` → entrega **sem webhooks** (só a reconciliação percebe). Taxa determinística por CEP em reais inteiros (`BaseFeeCents` + 100 × (soma dos dígitos mod 8)), para não interferir nos valores sandbox do pagamento.

**Configuração** (`Simulator:Delivery`): `ClientId`, `ClientSecret` (≥ 8), `CustomerId` (`cus_sim_fulfillmenthub`), `TokenLifetimeSeconds` 300, `WebhookSigningKey` (≥ 16), `WebhookUrl?`, `QuoteTtlSeconds` 900, `CourierAssignMs` 1000, `StepMs` 3000, `BaseFeeCents` 1200, `CouriersBusyRate` 0, `WebhookDuplicateRate` 0, `WebhookDelayMs` 0, `WebhookOutOfOrder` false, `IdempotencyTtlMinutes` 60. Valores dev-only em `appsettings.Development.json`. Não implementado: rota admin `POST /admin/scenario` (BL-066), `WebhookFailFirstN` (BL-245).

**Lado FulfillmentHub** (`Providers:Delivery:*` + `Fulfillment:Origin:*`): `IDeliveryProviderClient` → `SimulatedDeliveryProviderClient` com a pipeline compartilhada (§4) **por fora** e `DeliveryBearerTokenHandler` **por dentro** (`DeliveryAccessTokenProvider`: cache do token, renovação `TokenRefreshSkewSeconds` antes de expirar, 401 → renova uma vez e repete); erros 4xx viram `Failure` com `provider.<code>`, `409` traz `Metadata["delivery_id"]`. Fluxo: cotação no checkout (D-51, `CheckoutDeliveryQuoter`; provider fora → taxa estimada) → após `Paid` o Worker (`DeliveryRequestService`, D-52) chama `RequestDeliveryHandler`: reusa a cotação válida, recota **uma** vez se expirou (segunda expiração → `delivery.quote_expired`, pedido fica `Paid` para o operador), cria com `idempotency_key` durável (`order-{id}-delivery-{n}`), `409 duplicate_delivery` → `GET` e adota, rejeição permanente (`address_undeliverable`, `invalid_params`) → pedido `Cancelled(DeliveryFailed)` + estoque devolvido. Cancelamento do pedido com entrega ativa cancela no provider primeiro; `noncancelable_delivery` → `409 order.delivery_in_progress`.

## 3. Provider de pagamento simulado (rotas `/payments/v1/...`) — IMPLEMENTADO (Fase 5, 2026-09-18)

Contrato **próprio e minimalista**, inspirado no ciclo de vida comum de PSPs (intent → autorização → captura → estorno) — **não** modela nenhum PSP específico. Autenticação: `Authorization: Bearer <api key>` (`Simulator:Payments:ApiKey`); JSON em `snake_case`.

| Operação | Método/path | Request | Response |
|---|---|---|---|
| Criar pagamento | `POST /payments/v1/payments` (header `Idempotency-Key` **obrigatório** → `400 invalid_request` sem ele) | `amount` (centavos), `currency`, `order_reference`, `customer_reference`, `capture` (bool, default true), `scenario?` (`approve`/`decline`/`silent_approve`, só para testes manuais) | `201` `{ id: "pay_…", status: "pending", amount, currency, order_reference, failure_code, created_at, updated_at }`; mesma chave + mesmo corpo → `200` com o mesmo pagamento; mesma chave + corpo diferente → `409 idempotency_conflict` |
| Consultar | `GET /payments/v1/payments/{id}` | — | idem, `status` ∈ `pending/authorized/paid/failed/refunded`, `failure_code?` |
| Estornar | `POST /payments/v1/payments/{id}/refunds` | `amount?` (centavos; default = saldo) | `{ id: "ref_…", payment_id, status: "succeeded", amount, created_at }`; pagamento não `paid` → `422 not_refundable` |
| Webhook | `POST <Simulator:Payments:WebhookUrl>` | `{ "id": "evt_…", "type": "payment.status_changed", "created_at", "data": { "payment_id", "status", "failure_code"?, "order_reference", "occurred_at" } }`; headers `X-Signature` = HMAC-SHA256 hex(corpo bruto, `WebhookSigningKey`), `X-Timestamp` (unix s), `X-Event-Id` | esperado `2xx`; senão retry em 1 s / 2 s / 4 s e descarte (logado) |
| Erros | `{ code, message, kind: "error" }` — `400 invalid_request`, `401 unauthorized`, `404 not_found`, `409 idempotency_conflict`, `422 not_refundable`, `429 rate_limited` (+ `Retry-After`), `500 internal_server_error` (caos) | | |

**Liquidação**: todo pagamento nasce `pending` e, após `SettleDelayMs`, vira `paid` ou `failed` (`failure_code = DeclineCode`, default `card_declined`) e dispara o webhook.

**Regras de sandbox por valor** (como os "valores mágicos" de PSPs reais; `PaymentSimulatorStore.ScenarioFromAmount`): centavos terminados em **99** → recusado; terminados em **98** → aprovado **sem webhook** (simula webhook perdido — a reconciliação precisa perceber); qualquer outro → segue `ApprovalRate`. Um `scenario` explícito no request tem precedência.

**Configuração** (seção `Simulator:Payments`, `appsettings` / variáveis `Simulator__Payments__*`):

| Chave | Efeito | Default |
|---|---|---|
| `ApiKey` | chave esperada no `Authorization: Bearer` | vazio (obrigatória; `appsettings.Development.json` traz um valor **dev-only**) |
| `WebhookSigningKey` | chave HMAC dos webhooks (≥ 16 chars) | vazio (obrigatória; valor dev-only em Development) |
| `WebhookUrl` | destino dos webhooks; vazio = não envia | `http://localhost:5000/api/v1/webhooks/payments` em Development |
| `SettleDelayMs` | tempo até `paid`/`failed` | 2000 |
| `ApprovalRate` | fração aprovada (0–1) fora das regras por valor | 1 |
| `DeclineCode` | `failure_code` das recusas | `card_declined` |
| `WebhookDuplicateRate` | fração de webhooks enviados 2× | 0 |
| `WebhookDelayMs` | atraso antes do envio | 0 |
| `IdempotencyTtlMinutes` | TTL da `Idempotency-Key` | 60 |

Caos genérico (seção `Simulator:Chaos`, aplicado a todas as rotas do simulator): `LatencyMs`, `LatencyJitterMs`, `FailureRate` (500 aleatório), `TimeoutRate` (request "some"), `RateLimitPerMinute` (janela fixa compartilhada; 0 = desligado; 429 + `Retry-After` com o código de cada provider — `rate_limited` / `customer_limited`; implementado na Fase 6). Estado em memória (D-P8 resolvida); reiniciar limpa tudo.

## 4. Política de resiliência das chamadas de saída

| Elemento | Valor inicial (ajustável por Options) |
|---|---|
| Timeout total por operação | 15 s (quote/create), 8 s (get/cancel) |
| Timeout por tentativa | 5 s |
| Retry | máx. 3, exponencial base 500 ms, jitter (Polly `DelayBackoffType.Exponential` + `UseJitter`) |
| Predicado de retry | 408, 429 (com `Retry-After`), 500, 502, 503, 504, `HttpRequestException`, `TimeoutRejectedException`; **nunca** 400/401/403/404/409/422 |
| Circuit breaker | 50% de falhas em janela de 30 s com mínimo de 10 chamadas → aberto 30 s |
| Idempotência de POST | `idempotency_key`/`Idempotency-Key` sempre presente em criações |
| Token | cache em memória com renovação antecipada; 401 → renovar uma vez e repetir |
| Telemetria | span por chamada (`peer.service`, status, tentativa), métricas `provider.request.duration`, `provider.retry.count`, `provider.circuit.state` |

**Implementação (Fase 5, pagamento; Fase 6 extraiu a pipeline para `AddProviderResilienceHandler<TOptions>` compartilhada com o delivery client, opções base `ProviderResilienceOptions` + `CircuitBreakDurationSeconds`)**: `AddFulfillmentHubPaymentProvider()` — typed client `IPaymentGatewayClient` com `Microsoft.Extensions.Http.Resilience`: timeout total (`Providers:Payment:TotalTimeoutSeconds`, 15) → retry (`MaxRetryAttempts` 3, base `RetryBaseDelayMs` 500, exponencial + jitter, honra `Retry-After`; predicado exatamente o da tabela; `0` desliga o retry) → circuit breaker (50 % / 30 s / mín. 10 / aberto 30 s) → timeout por tentativa (`AttemptTimeoutSeconds`, 5). `HttpClient.Timeout` fica infinito: os timeouts pertencem à pipeline. O `Idempotency-Key` enviado é `Payment.ProviderIdempotencyKey` (estável por pagamento, então retries e a reconciliação nunca criam um segundo pagamento no provider). Falhas de transporte/timeout/circuito aberto viram `Failure.Unavailable("provider.unavailable")`; 4xx de contrato viram `Validation/Forbidden/NotFound/Conflict` e **não** são repetidos. Token: não se aplica ao pagamento (API key estática); no delivery, `DeliveryAccessTokenProvider` + `DeliveryBearerTokenHandler` (Fase 6, §2.4). Telemetria: spans `Provider CreatePayment/GetPayment/RefundPayment` + instrumentação padrão de `HttpClient` (duração/status por tentativa); métricas dedicadas de retry/circuito ficaram para a Fase 11 (BL-246). Evidência: `PaymentGatewayClientTests` (T9/T10).

## 5. Ingestão de webhooks (entrada)

1. Ler corpo bruto (buffer) → validar assinatura HMAC (comparação em tempo constante) e tolerância de timestamp (5 min) → 401 se inválida.
2. Extrair `provider_event_id`; `INSERT` em `webhook_events` (`UNIQUE`) → duplicado: `200` imediato (métrica `webhook.duplicate`).
3. Fases 6–8: processar in-process após persistir (mesmo request, mas o `200` não depende do processamento — se falhar, o worker reprocessa por varredura de `status=Received`).
   Fase 9: publicar em `fh-webhooks-inbound` (SQS) e responder `200`.
4. Worker aplica ao agregado com as regras de ordem (DOMAIN.md §7); marca `processed_at`; falha → `attempts++`, backoff; N falhas → `Failed` (Admin).
5. Endpoint de webhook: sem autenticação JWT (usa assinatura), rate limit próprio, tamanho máximo de corpo (64 KB), sem logar corpo completo (apenas ids/status).

**Implementação (Fase 5, `POST /api/v1/webhooks/payments`)**: passos 1, 2 e 5 como descritos (`WebhookSignatureVerifier`: `CryptographicOperations.FixedTimeEquals`, tolerância `Providers:Payment:WebhookTimestampToleranceSeconds` = 300; `WebhookInbox` grava em escopo próprio antes de processar; rate limit `webhooks` 120/min por IP; corpo > 64 KB → 413). Passo 3: processamento in-process no mesmo request (`ApplyPaymentWebhookHandler`); o `200` é devolvido mesmo se o processamento falhar — o evento fica `Failed` com `last_error` e a **reconciliação** (Worker) corrige o estado consultando o provider. Eventos de tipo desconhecido ou pagamento desconhecido → `Ignored`. **D-P5 resolvida = sim**: `paid`/`refunded` são confirmados com `GET` no provider antes de serem aplicados; o status que vale é o do provider, não o do corpo do webhook (`Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted`). A varredura de `status=Received/Failed` pelo worker (passo 4) fica para a Fase 8, junto com a outbox.

**Implementação (Fase 7, `POST /api/v1/webhooks/deliveries`)**: a pipeline de recepção foi extraída para `WebhookReceiver` (Api) e é compartilhada pelos dois endpoints — cada um só declara a `WebhookSource` (provider, header de assinatura, chave, tolerância) e como identificar/processar o payload. Entregas: header **`X-Uber-Signature`**, chave `Providers:Delivery:WebhookSigningKey`, payload `event.delivery_status` (`id`, `kind`, `created`, `status`, `delivery_id`, `data{id,status,updated,courier{name,vehicle_type,phone_number,location},tracking_url}`); dedup por `id`; `ApplyDeliveryWebhookHandler` localiza a `Delivery` por `(provider, provider_delivery_id)` e `DeliveryStatusApplier` aplica via `Delivery.ApplyProviderEvent` — a disposição (`Applied/Duplicate/OutOfOrder/Stale/Conflict`, DOMAIN.md §7) decide se o pedido acompanha (`pickup_complete`/`dropoff` → `InDelivery`, `delivered` → `Delivered` (passando por `InDelivery` se o pickup se perdeu), `canceled`/`returned` → `Cancelled(DeliveryFailed)` + estoque devolvido). Eventos fora de ordem contam em `fh.webhooks.out_of_order{disposition}`. **D-59**: ao contrário do pagamento, o status de entrega **não** é confirmado com `GET` antes de aplicar (não é dinheiro; as regras de ordem do agregado limitam o dano). Passo 4 (worker): `ReconcileDeliveriesHandler` relê no provider entregas ativas sem evento há `Worker:DeliveryReconciliation:QuietForSeconds` (300) e aplica o status atual como evento sintético `reconciled:<id>:<status>:<updated>` pelas mesmas regras (`LostWebhooks_AreRecoveredByReconciliation`). Simulator: `Simulator:Delivery:WebhookUrl` ligado em Development; CEP `…003` = entrega silenciosa (sem webhooks) para exercitar a reconciliação.

## 6. Quando usar fila vs. chamada síncrona (trade-off documentado)

| Operação | Modo | Por quê |
|---|---|---|
| Cotação de entrega ao criar pedido | síncrona | usuário precisa da taxa para confirmar; resposta rápida; falha → 503 controlado |
| Criar pagamento | assíncrona (outbox) | não bloquear o `POST /orders`; retry sem o cliente esperar |
| Criar entrega | assíncrona (evento `OrderPaid`) | só após pagamento; retries longos |
| Processar webhooks | assíncrona (persistir → fila) | responder ao provider em ms; reprocessamento; isolar falhas |
| Cancelar entrega pelo operador | síncrona (com retry curto) | operador espera confirmação; fallback: marcar "cancelamento pendente" e worker tenta |
| Reconciliação | job periódico | varredura de pendentes |
