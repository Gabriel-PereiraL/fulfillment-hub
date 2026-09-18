# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 8 — Fase 7)

## Fase atual
**Fase 7 — Webhooks de entrega + idempotência + fora de ordem: CONCLUÍDA (Gate 7 fechado).**
Próxima fase: **Fase 8 — Transactional outbox + Worker** (não iniciada).

## Concluído (Fase 7)
- Api/Webhooks: `WebhookReceiver` (pipeline compartilhada: 64 KB → HMAC em tempo constante + tolerância → parse → `WebhookInbox` dedup → processa → sempre 200; `WebhookSource`, `WebhookIdentity`, `WebhookOutcome.Processed/Ignored/Failed`; logs 5200–5202); `PaymentWebhooksEndpoints` reescrito sobre ele (pagamento desconhecido → `Ignored`); **`DeliveryWebhooksEndpoints`** `POST /api/v1/webhooks/deliveries` (`X-Uber-Signature`, chave `Providers:Delivery:WebhookSigningKey`, tolerância `WebhookTimestampToleranceSeconds`, payload `event.delivery_status` com `data.courier`).
- Application: `WebhooksMetrics` (`fh.webhooks.received/rejected/duplicates/out_of_order{disposition}`) separado de `PaymentsMetrics`; `Deliveries/DeliveryStatusApplier` (único ponto que aplica status de entrega: `Delivery.ApplyProviderEvent` → disposição; só `Applied` move o pedido: `pickup_complete`/`dropoff` → `InDelivery`, `delivered` → `Delivered` (via `InDelivery` se o pickup se perdeu), `canceled`/`returned` → `Cancelled(DeliveryFailed)` + `StockRelease`; métrica `fh.deliveries.events{disposition}`; logs 6200–6203), `ApplyDeliveryWebhookHandler` (D-59: sem `GET` antes de aplicar), `ReconcileDeliveriesHandler` (entregas ativas com `UpdatedAt` ≤ cutoff → `GET` → evento sintético `reconciled:<id>:<status>:<updated>`; logs 6030–6032); `ProviderDeliveryEvent` record; `SimulatedDeliveryProviderClient.MapCourier` público.
- Worker: base `PeriodicJob` (escopo por execução, erro logado e loop continua, logs 2100–2102 com o nome do job); `PaymentReconciliationService`, `DeliveryRequestService` e o novo `DeliveryReconciliationService` (`Worker:DeliveryReconciliation`: `IntervalSeconds` 60, `QuietForSeconds` 300, `BatchSize` 50) reescritos sobre ela.
- Simulator: `SimulatedDelivery.SendWebhooks` + CEP `…003` (entrega silenciosa, D-63); `Simulator:Delivery:WebhookUrl` = `http://localhost:5000/api/v1/webhooks/deliveries` em Development.
- Testes: **217 no total** (124 unit, 5 architecture, 88 integration). Novos `DeliveryWebhookTests` (6): ponta a ponta pedido → `Delivered` por webhooks reais do simulator (4 eventos `Applied`, courier com telefone mascarado, 1 transição `Delivered`); `returned` → `Cancelled(DeliveryFailed)` + estoque devolvido; **T7** `delivered` antes de `pickup` → `Delivered`, `pickup` registrado `Stale`, duplicado do mesmo `id` → 1 linha em `webhook_events` e 1 transição; chave do provider de pagamento / header `X-Signature` / timestamp velho → 401 sem persistir; entrega desconhecida → 200 `Ignored`; webhooks perdidos (CEP `…003`) recuperados por `ReconcileDeliveriesHandler` (evento `reconciled:*`). Fixture: `Simulator:Delivery:WebhookUrl` ligado (`http://api.test/api/v1/webhooks/deliveries`), `CourierAssignMs`/`StepMs` = 500; `GetWebhookEventsAsync(api, id, provider)`; `SignedWebhook(path, signatureHeader)`; testes da Fase 6 ajustados aos webhooks ao vivo (`CancelOrder_OnceTheCourierHasTheParcel…` usa CEP silencioso para provar a recusa do provider).
- Verificação manual em Kestrel (API + simulator + worker): `AwaitingPayment` (t+0) → `Paid` (t+3 s) → `DeliveryRequested` (t+9 s, worker) → `InDelivery` (t+12 s) → `Delivered` (t+18 s); 4 webhooks `X-Uber-Signature` aplicados, 0 rejeitados; 0 erros; 0 segredos nos logs. Build 0 warnings; format limpo; sem pacotes vulneráveis.

## Em andamento
- Nada.

## Próximas tarefas (Fase 8 — Transactional outbox + Worker; ler antes ADR-004, ADR-010, `fulfillmenthub-dotnet` §9, `messaging`, `csharp-concurrency-patterns`, `ef-core`, DOMAIN.md §9 (registros de infraestrutura), OBSERVABILITY (métricas de outbox))
1. `OutboxMessage` (`outbox_messages`: `id`, `type`, `payload` jsonb, `occurred_at`, `processed_at`, `attempts`, `next_attempt_at`, `last_error`, `status Pending/Processed/Failed`, índice `(status, next_attempt_at)`) + migration; `SaveChangesInterceptor` que coleta `DomainEvents` dos agregados rastreados (`OrderPlaced`, `OrderPaid`, `OrderDeliveryRequested`, `OrderCancelled`, `OrderDelivered`) e insere na mesma transação; limpar eventos após o commit.
2. Worker `OutboxPublisher` (`PeriodicJob`, intervalo curto ~500 ms): lote `FOR UPDATE SKIP LOCKED` (`FromSql`), despacho in-process por tipo (`IOutboxHandler<TEvent>`), backoff exponencial + jitter, `Failed` após N (`Worker:Outbox:MaxAttempts`), métricas `fh.outbox.pending/failed/lag/publish.duration`.
3. Migrar os disparos in-process: `OrderPlaced` → `CreatePaymentForOrderHandler` (tirar de `PlaceOrderAsync`; a resposta do `POST /orders` volta a `Created` — atualizar testes/docs, consistência eventual documentada no PRODUCT), `OrderPaid` → `RequestDeliveryHandler` (substitui a varredura `DeliveryRequestService`, que pode ficar como reconciliação com intervalo maior), `OrderCancelled` → estorno quando o pagamento estava `Paid` (fecha BL-244 usando `IPaymentGatewayClient.RefundAsync`).
4. Endpoint admin de reprocessamento (`POST /api/v1/admin/outbox/{id}/retry`, AdminOnly) ou comando do Worker.
5. Testes: T12 (exceção injetada após `SaveChanges` e antes do publish → mensagem continua na outbox e é publicada), T13 (handler lança 3× → `Failed`; reprocessar → sucesso), consumidor idempotente (mesma mensagem 2× → um efeito), fluxo completo pedido → entregue só via outbox.
6. Docs: ADR-004 "Implementação", DOMAIN §9/§12, INTEGRATIONS §6 (o que ficou síncrono: cotação no checkout), OBSERVABILITY (outbox ✔), SECURITY (endpoint admin), TEST_STRATEGY T12/T13, DECISIONS, BACKLOG (BL-146, 244), ROADMAP; fechar Gate 8.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-59…D-63)
- D-59 entrega aplicada sem `GET` prévio (assinatura + ordem do agregado + reconciliação); D-60 `WebhookReceiver` unificado + `WebhooksMetrics`; D-61 pedido acompanha só eventos `Applied`; D-62 reconciliação como evento sintético + base `PeriodicJob`; D-63 CEP `…003` silencioso e webhooks ligados em Development/fixture.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P4 k6/NBomber · D-P6 rede AWS dev · D-P7 estado Terraform.

## Testes atuais
- 217 testes verdes (~50 s a quente). Matriz TEST_STRATEGY: T1–T11 ✔, T16–T18 ✔; T12/T13 são o alvo da Fase 8.

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco com 4 migrations (nenhuma nova nas Fases 6–7) e seed; user-secrets da Api/Worker: `Database:ConnectionString`, `Jwt:SigningKey` (Api), `Seed:*Password` (Api), `Providers:Payment:*`, `Providers:Delivery:*`. Nenhum remote/GitHub/AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 7 — fechado.** Critérios: webhook fora de ordem ✔ (T7) e duplicado ✔ com testes de integração; assinatura inválida → 401 ✔; pedido acompanha `InDelivery`/`Delivered`/cancelado por `returned` ✔; fluxo pedido → entregue ponta a ponta com o simulator in-process ✔; BACKLOG P0 fechado ✔ (BL-065, BL-144; BL-067 P1 também).
**Gate 8 — Outbox**: nenhum efeito externo fora do worker (exceto a cotação síncrona no checkout, documentada); teste de "crash após commit" passa (T12); handler falho reexecutado e `Failed` após N (T13); ADR-004 atualizado com o que foi implementado.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 8), com `docker compose --profile deps up -d` ativo.
