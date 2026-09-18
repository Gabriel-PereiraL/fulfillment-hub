# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 9 — Fase 8)

## Fase atual
**Fase 8 — Transactional outbox + Worker: CONCLUÍDA (Gate 8 fechado).**
Próxima fase: **Fase 9 — SQS (LocalStack)** (não iniciada).

## Concluído (Fase 8)
- Domain: `IAggregateRoot` (visão não genérica dos eventos), evento `PaymentPaid` (levantado em `Payment.ApplyProviderStatus(Paid)`).
- Application/Outbox: `IOutboxHandler` + `OutboxHandler<TEvent>` + `OutboxHandling.Done/Retry`; handlers `OrderPlaced` → `CreatePaymentForOrderHandler`, `OrderPaid` → `RequestDeliveryHandler`, `OrderCancelled` e `PaymentPaid` → `RefundPaymentHandler` (novo; fecha BL-244); registrados por chave `AddKeyedScoped<IOutboxHandler, T>(nameof(Evento))`; `OutboxMetrics` (`fh.outbox.published{outcome,type}`, `lag`, `publish.duration`, gauges `pending/failed`).
- Infrastructure/Outbox: `OutboxMessage` (`outbox_messages`: payload jsonb, `aggregate_id`, `attempts`, `next_attempt_at`, `locked_until`, `last_error`, `correlation_id`, `trace_parent`, índice `(status, next_attempt_at)`, xmin) + migration `20260918170717_OutboxMessages` (aplicada no banco local); `OutboxInterceptor` (`SaveChangesInterceptor` no `AddDbContext`: eventos → linhas no mesmo commit, limpos após o commit; correlation id da request e `Activity.Id`); `OutboxEventSerializer` (STJ, ids fortes como GUID, `Money` `{amount,currency}`, registro por nome CLR); `OutboxProcessor` (`SELECT …, xmin … FOR UPDATE SKIP LOCKED` + lease `Outbox:LeaseSeconds`, escopo DI por mensagem, backoff exponencial com jitter `BaseDelaySeconds`/`MaxDelaySeconds`, `Failed` após `MaxAttempts`, span `Outbox <type>` com parent = `trace_parent`; logs 7000–7002); `AddFulfillmentHubOutboxPublisher()`.
- Worker: `OutboxPublisherService : PeriodicJob` (`Worker:Outbox:IntervalMs` 500); `DeliveryRequestService` mantido a 60 s como rede de segurança (D-68).
- Api: `POST /orders` responde `Created` (pagamento sai pela outbox; D-64); `Admin/OutboxAdminEndpoints` (`GET /api/v1/admin/outbox?status=`, `POST /api/v1/admin/outbox/{id}/retry`, AdminOnly, log 7100); `RateLimitOptions` (`RateLimiting:LoginPerMinute` 5, `WebhooksPerMinute` 1200 — D-69, achado na suíte: 120/min derrubava webhooks de entrega).
- Testes: **222 no total** (124 unit, 5 architecture, 93 integration). Novos `OutboxTests` (6): T12 (mensagem gravada no mesmo commit com correlation id, nenhum efeito no request, publicada depois; provider fora na 1ª publicação → `Pending`/`attempts=1`, sucesso depois), T13 (3 falhas → `Failed`, listado no admin, requeue → processado), admin só para Admin, redelivery de mensagem processada → 1 pagamento, cancelar pedido pago → estorno pela outbox. Fixture: publisher pausável hospedado no test host (`OutboxControl.Pause()/RunOnceAsync()`, começa pausado até as migrations), `Outbox:MaxAttempts=3`/`BaseDelaySeconds=1`, `CircuitBreakDurationSeconds=1` nos dois providers; testes das Fases 5–7 adaptados aos gatilhos assíncronos (`PlacePaidOrderAsync` pausa a outbox e publica o `OrderPlaced` à mão).
- Verificação manual em Kestrel: `Created` (t+0) → `AwaitingPayment` (t+3 s) → `Paid` (t+5 s) → `DeliveryRequested` (t+6 s) → `InDelivery` → `Delivered` (t+18 s) só pela outbox + webhooks; admin lista `OrderPlaced/OrderPaid/PaymentPaid/OrderDeliveryRequested/OrderDelivered` processados e nenhum `Failed`; 0 erros, 0 segredos. ADR-004 com seção "Implementação".

## Em andamento
- Nada.

## Próximas tarefas (Fase 9 — SQS; ler antes ADR-005, ARCHITECTURE §filas, INTEGRATIONS §6, `fulfillmenthub-dotnet` §9, `messaging`, `csharp-concurrency-patterns`, `testcontainers`)
1. `Messaging:Sqs` options (`Enabled`, `ServiceUrl` LocalStack, região, credenciais fake, nomes das filas `fh-domain-events`/`fh-webhooks-inbound` + DLQs, `MaxReceiveCount` 5, `VisibilityTimeoutSeconds`, `MaxConcurrency`); `SqsQueueProvisioner` (cria DLQ + fila com `RedrivePolicy`, idempotente) no start do Worker; LocalStack no `docker-compose.yml` (perfil `deps`).
2. `IMessagePublisher` (AWS SDK `AWSSDK.SQS`): envelope JSON `{id,type,payload,occurredAt,correlationId,traceParent}` + atributos; `OutboxProcessor` publica no SQS quando `Enabled` (in-process continua como modo sem fila, D-70).
3. Consumidor base no Worker (long polling, `VisibilityTimeout` > processamento, `SemaphoreSlim` de concorrência, `ChangeMessageVisibility` em falha, delete só após sucesso); `DomainEventsConsumer` → `IOutboxHandler` por tipo com dedup persistida em `processed_messages (consumer, message_id)` na mesma transação do efeito; migration.
4. Webhooks: API persiste no inbox → publica `{webhookEventId, provider}` em `fh-webhooks-inbound` → `WebhooksInboundConsumer` processa (`IWebhookProcessor` por provider, extraído dos endpoints para Infrastructure); fallback in-process quando SQS desligado/indisponível.
5. Métricas `fh.queue.messages.processed/failed{queue}`, `fh.queue.message.age`, `fh.queue.dlq.depth`; propagação de trace (BL-088 parcial).
6. Testes com Testcontainers LocalStack: mensagem envenenada → DLQ após `maxReceiveCount`; duplicata entregue 2× → um efeito; fluxo pedido → pago via SQS; compose completo sobe (smoke).
7. Docs: ADR-005 "Implementação", INTEGRATIONS §5/§6, ARCHITECTURE, OBSERVABILITY, DEVELOPMENT (LocalStack), TEST_STRATEGY T14/T15, DECISIONS, BACKLOG (BL-084…088), ROADMAP; fechar Gate 9.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-64…D-69)
- D-64 outbox própria (interceptor + processor + handlers por chave, lease + `SKIP LOCKED`, `POST /orders` → `Created`); D-65 `Done`/`Retry`, `Failed` após N, admin requeue; D-66 estorno por `OrderCancelled` e `PaymentPaid`; D-67 serialização STJ por nome CLR; D-68 varredura de entregas mantida como rede de segurança; D-69 rate limit de webhooks configurável (1200/min).

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P4 k6/NBomber · D-P6 rede AWS dev · D-P7 estado Terraform.

## Testes atuais
- 217 testes verdes (~50 s a quente). Matriz TEST_STRATEGY: T1–T11 ✔, T16–T18 ✔; T12/T13 são o alvo da Fase 8.

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco com 5 migrations (`OutboxMessages` aplicada) e seed; user-secrets da Api/Worker: `Database:ConnectionString`, `Jwt:SigningKey` (Api), `Seed:*Password` (Api), `Providers:Payment:*`, `Providers:Delivery:*`. Nenhum remote/GitHub/AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 8 — fechado.** Critérios: nenhum efeito externo fora do worker ✔ (exceções documentadas: cotação no checkout e cancelamento no provider, INTEGRATIONS §6); "crash após commit" ✔ (T12); handler falho reexecutado e `Failed` após N ✔ (T13); ADR-004 atualizado ✔; BACKLOG P0 fechado ✔ (BL-080…083, 146).
**Gate 9 — SQS**: mensagem envenenada vai à DLQ após `maxReceiveCount`; consumidor recebe duplicata e não duplica efeito; compose completo (Postgres + LocalStack + Aspire + hosts) funciona; testes de integração com Testcontainers LocalStack.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 9), com `docker compose --profile deps up -d` ativo.
