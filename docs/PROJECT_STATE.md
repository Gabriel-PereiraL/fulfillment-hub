# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 7 — Fase 6)

## Fase atual
**Fase 6 — Delivery provider simulator + integração de saída: CONCLUÍDA (Gate 6 fechado).**
Próxima fase: **Fase 7 — Webhooks de entrega + idempotência + fora de ordem** (não iniciada).

## Concluído (Fase 6)
- ProviderSimulator `/delivery/v1` (`Deliveries/`): `POST /delivery/oauth/token` (client credentials fake, token opaco de 300 s), `POST /customers/{id}/delivery_quotes`, `POST /customers/{id}/deliveries` (`quote_id`, `idempotency_key`, `external_id`), `GET /deliveries/{id}`, `POST /deliveries/{id}/cancel`; nomes de campo do contrato de referência (endereços como string JSON estruturada); erros `invalid_params`, `address_undeliverable`, `expired_quote`, `used_quote`, `duplicate_delivery` (409 + `metadata.delivery_id`), `noncancelable_delivery`, `customer_not_found`, `delivery_not_found`, `unauthorized`, `customer_limited` (429), `couriers_busy` (503); `DeliveryLifecycleService` (`pending → pickup → pickup_complete → dropoff → delivered`/`returned`, `CourierAssignMs`/`StepMs`) emitindo `event.delivery_status` assinado em `X-Uber-Signature` quando `WebhookUrl` está definido (vazio até a Fase 7); regras de sandbox por CEP (`00000…` recusado, `…001` cotação de 1 s, `…002` devolvida); taxas determinísticas em reais inteiros (D-58). `ChaosFilter` agora implementa `RateLimitPerMinute` (janela compartilhada, código por provider); `OutgoingWebhook.SignatureHeader` por provider.
- Application/Deliveries: `IDeliveryProviderClient` (+ DTOs `DeliveryParty`, `GatewayQuote*`, `GatewayCreateDelivery`, `GatewayDelivery`), `FulfillmentOriginOptions` (`Fulfillment:Origin`, loja única + `EstimatedDeliveryFee`), `CheckoutDeliveryQuoter` (D-51: cotação no checkout; provider fora → taxa estimada; endereço recusado → `order.address_undeliverable`), `RequestDeliveryHandler` (reusa cotação válida, recota 1×, `Delivery.Request` com chave durável antes da chamada, adota `409`, permanente → `Cancelled(DeliveryFailed)` + estoque), `RequestPendingDeliveriesHandler` (varredura de `Paid` sem `DeliveryId`), `DeliveriesMetrics` (`fh.deliveries.quotes/requested{outcome}`); `Failure.Metadata`; `StockRelease` compartilhado; `PlaceOrderHandler` cota antes da transação e grava `DeliveryQuote`; `Order.SetDeliveryFee` (só em `Created`); `CancelOrderHandler` cancela a entrega ativa no provider (`noncancelable_delivery` → 409 `order.delivery_in_progress`, D-57).
- Infrastructure: `Providers/ProviderResilienceOptions` + `AddProviderResilienceHandler<TOptions>` (pipeline compartilhada, `CircuitBreakDurationSeconds`, `HttpRequestException` com status segue a regra de status, D-53/D-54); `Providers/Deliveries`: `DeliveryProviderOptions` (`Providers:Delivery:*`), contratos wire, `DeliveryAccessTokenProvider` (cache + renovação antecipada + um renovador por vez), `DeliveryBearerTokenHandler` (401 → renova 1× e repete, dentro da pipeline), `SimulatedDeliveryProviderClient` (spans `Provider CreateQuote/CreateDelivery/GetDelivery/CancelDelivery`), `AddFulfillmentHubDeliveryProvider()` (typed client + client nomeado `delivery-provider-auth` + `Fulfillment:Origin`).
- Worker: `DeliveryRequestService` (`Worker:DeliveryRequests`: `IntervalSeconds` 10, `BatchSize` 50; logs 2200–2202), D-52. Api/Worker `appsettings.json` com `Providers:Delivery` (chaves vazias) e `Fulfillment:Origin` (loja fictícia em Recife).
- Testes: **211 no total** (124 unit, 5 architecture, 82 integration). Novos: `DeliveryProviderClientTests` (11: token uma vez e reusado; 401 → renova e repete; cotação mapeada/endereço estruturado; 4 erros de contrato sem retry; 409 com metadata; 429 `Retry-After`; `couriers_busy` ×4 → `Unavailable` com a mesma chave; **CB abre e fecha após probe half-open** — T10 completo); `DeliveryFlowTests` (10: cotação no checkout cobrada no pagamento; provider fora → taxa estimada 15,00; CEP recusado → 400 sem reservar; `Paid → DeliveryRequested` reusando a cotação; T11 recotação com cotação de 1 s; T11 segunda expiração → `delivery.quote_expired` e pedido `Paid`; `409 duplicate_delivery` → adota a entrega pré-criada no simulator; `address_undeliverable` na criação → `Cancelled(DeliveryFailed)` + estoque; cancelar com entrega ativa cancela no provider; cancelar após `pickup_complete` → 409); `OrderTests.SetDeliveryFee_*` (3). `ScriptedTransport` movido para `tests/FulfillmentHub.UnitTests/Support`; `ProviderOutage.Script` para respostas forjadas por teste; `OrderBody(postalCode, …)`.
- Verificação manual em Kestrel: pedido → `deliveryFee` R$17,00 cotada no checkout, `total` 56,50 → `Paid` pelo webhook → Worker pediu a entrega em ~7 s (`DeliveryRequested`, `deliveryId`), token renovado 1×, pedido antigo da Fase 5 recotado e enviado; 0 erros, 0 segredos nos logs. Build 0 warnings; format limpo; sem pacotes vulneráveis. User-secrets `Providers:Delivery:*` definidos para Api e Worker.
- Correção de docs: as inserções de D-42…D-50 (DECISIONS) e da "Implementação (Fase 5)" de INTEGRATIONS §4 tinham ido parar no topo dos arquivos (perl com `\|` dentro de `s|…|`); reposicionadas.

## Em andamento
- Nada.

## Próximas tarefas (Fase 7 — Webhooks de entrega; ler antes `fulfillmenthub-dotnet` §8/§11, `minimal-api`, `testing`, INTEGRATIONS.md §1.5/§2.4/§5, DOMAIN.md §7 (ordem canônica), SECURITY.md §2b)
1. `POST /api/v1/webhooks/deliveries` reutilizando `WebhookInbox`/`WebhookSignatureVerifier` (header `X-Uber-Signature`, chave `Providers:Delivery:WebhookSigningKey`, mesmo rate limit/64 KB); payload `event.delivery_status` (`id`, `kind`, `created`, `status`, `delivery_id`, `data{…courier…}`); dedup por `id`; sempre 200 após persistir.
2. `ApplyDeliveryWebhookHandler`: localizar `Delivery` por `(provider, ProviderDeliveryId)`; `Delivery.ApplyProviderEvent(eventId, status, mapped, occurredAt = data.updated, courier, now)` com disposição (`Applied/Duplicate/OutOfOrder/Stale/Conflict`) + métrica `fh.webhooks.out_of_order`; `Order` acompanha: `pickup_complete` → `MarkInDelivery`, `delivered` → `MarkDelivered`, `canceled`/`returned` → `Cancel(DeliveryFailed)` + estoque (+ estorno em BL-244) — só quando a disposição for `Applied`.
3. Simulator: ligar `Simulator:Delivery:WebhookUrl` (Development: `http://localhost:5000/api/v1/webhooks/deliveries`; fixture: `http://api.test/...`), cenários `WebhookDuplicateRate`, `WebhookDelayMs`, `WebhookOutOfOrder` (já emitidos); opcional BL-066 rota admin `POST /admin/scenario`.
4. Reconciliação de entregas no Worker (BL-067 parte 2): entregas ativas sem evento há > X → `GET` no provider e aplica (mesmo `PaymentReconciliationService` pattern).
5. Testes: T7 (`delivered` antes de `pickup` → `Delivered`, evento `pickup` posterior `Stale`/`OutOfOrder`), duplicado → no-op, assinatura inválida → 401, fluxo completo pedido → entregue com o simulator in-process (webhooks reais, `StepMs` curto), `returned` → pedido cancelado + estoque; unit de `Delivery.ApplyProviderEvent` já existe (Fase 2) — conferir cobertura das disposições.
6. Docs: INTEGRATIONS §5 (entregas), SECURITY §2b (segundo provider/chave), TEST_STRATEGY T7 ✔, OBSERVABILITY (`out_of_order`), DECISIONS, BACKLOG (BL-144 restante, BL-065, BL-067), ROADMAP; fechar Gate 7.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-51…D-58)
- D-51 D-P3 resolvida: cotação no checkout + taxa estimada como fallback, loja única em `Fulfillment:Origin`; D-52 entrega pedida pelo Worker (varredura até a outbox); D-53 pipeline de resiliência compartilhada; D-54 token cache + handler dentro da pipeline; D-55 recotação única e rejeições permanentes cancelam; D-56 `409` adota a entrega existente (`Failure.Metadata`); D-57 cancelar no provider antes de cancelar localmente; D-58 taxas determinísticas/regras por CEP no simulator.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P4 k6/NBomber · D-P6 rede AWS dev · D-P7 estado Terraform.

## Testes atuais
- 211 testes verdes (~45 s a quente). Matriz TEST_STRATEGY: T1–T6 ✔, T8–T11 ✔, T16–T18 ✔; T7 é o alvo da Fase 7.

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco com 4 migrations (sem migration nova na Fase 6 — `delivery_quotes`/`deliveries` existem desde `DomainModel`) e seed; user-secrets da Api: `Database:ConnectionString`, `Jwt:SigningKey`, `Seed:*Password`, `Providers:Payment:*`, `Providers:Delivery:ClientId/ClientSecret/WebhookSigningKey`; do Worker: `Database:ConnectionString`, `Providers:Payment:*`, `Providers:Delivery:*`. Nenhum remote/GitHub/AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 6 — fechado.** Critérios: matriz de retry do delivery client coberta ✔ (`DeliveryProviderClientTests`); circuit breaker abre **e fecha** ✔; cotação expirada recotada ✔ (T11); `409 duplicate_delivery` reconciliado ✔; disclaimer no README do simulator ✔; `Paid → DeliveryRequested` com simulator in-process ✔; BACKLOG P0 de Delivery fechado ✔ (BL-062, 063, 064; BL-065 parcial — restante é Fase 7).
**Gate 7 — Webhooks de entrega**: "webhook fora de ordem" e "duplicado" com testes de integração passando (T7, T6 para entrega); assinatura inválida → 401; pedido acompanha `InDelivery`/`Delivered`/cancelado por `returned`; fluxo pedido → entregue de ponta a ponta com o simulator in-process.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 7), com `docker compose --profile deps up -d` ativo.
