# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 9 — Fases 8 e 9 + publicação)

## Fase atual
**Fase 9 — SQS (LocalStack): CONCLUÍDA (Gate 9 fechado).**
Próxima fase: **Fase 10 — Security hardening** (não iniciada). Repositório público em https://github.com/Gabriel-PereiraL/fullfillmentHub (primeira publicação nesta sessão).

## Concluído (Fase 9)
- Application/Messaging: `Queues` (`fh-domain-events`, `fh-webhooks-inbound`), `MessageEnvelope`, `IMessagePublisher` (`IsEnabled`), `QueueMetrics` (`fh.queue.messages.processed/failed{queue,consumer}`, `fh.queue.message.age`, gauge `fh.queue.dlq.depth`); `Webhooks/WebhookOutcome` compartilhado.
- Infrastructure/Messaging: `SqsOptions` (`Messaging:Sqs`: `Enabled`, `ServiceUrl`, `Region`, credenciais só para LocalStack, `QueuePrefix`, `MaxReceiveCount` 5, `VisibilityTimeoutSeconds` 60, `WaitTimeSeconds` 20, `BatchSize` 10, `MaxConcurrency` 4, `RetryBaseDelaySeconds`/`RetryMaxDelaySeconds`), `AddFulfillmentHubMessaging()` (`IAmazonSQS`, `SqsQueueProvisioner` — DLQ + fila com `RedrivePolicy`, idempotente, cache de URLs —, `SqsMessagePublisher` ou `NoOpMessagePublisher`), `ProcessedMessage` (`processed_messages` PK `(consumer, message_id)`) + migration `20260918173803_ProcessedMessages` (aplicada localmente); `Outbox/OutboxDispatcher` (handler em escopo próprio; com dedup: `processed_messages` + efeito na mesma transação, rollback em `Retry`, PK em corrida = duplicata); `OutboxProcessor` publica no SQS quando habilitado; `Webhooks/IWebhookProcessor` + `WebhookEventProcessor` (marca o inbox uma vez; evento não-`Received` é reconhecido sem trabalho); `PaymentWebhookProcessor`/`DeliveryWebhookProcessor` (parsing/aplicação extraídos dos endpoints, chaveados por provider); `OpenTelemetry.Instrumentation.AWS`. Pacotes: `AWSSDK.SQS` 4.0.100.14, `Testcontainers.LocalStack` 4.15.0, `OpenTelemetry.Instrumentation.AWS` 1.18.0.
- Worker/Messaging: `SqsConsumer` base (long polling, `SemaphoreSlim`, delete após sucesso, `ChangeMessageVisibility` com backoff, DLQ depth a cada 30 s, spans `receive` ligados ao `traceparent`; logs 8100–8104), `DomainEventsConsumer` (dedup `domain-events`), `WebhooksInboundConsumer` (ponteiro `{webhookEventId, provider}`; sempre reconhece — D-74), `SqsProvisioningService` (antes dos consumidores).
- Api: `WebhookReceiver` persiste → publica ponteiro em `fh-webhooks-inbound` → fallback in-process se broker desligado/indisponível (log 5203); endpoints de webhook reduzidos a `WebhookSource` + processor chaveado. `appsettings.Development.json` (Api/Worker) com `Messaging:Sqs` ligado para o LocalStack (`http://localhost:4566`, credenciais placeholder `test`). `docker-compose.yml`: serviço `localstack` (`localstack/localstack:4`, `SERVICES=sqs`, healthcheck) no perfil `deps`; `.env.example` `LOCALSTACK_PORT`.
- Testes: **225 no total** (124 unit, 5 architecture, 96 integration). `SqsApiFixture` (herda `ApiFixture` via hooks `ConfigureSettings`/`ConfigureTestServices`; LocalStack Testcontainers; consumidores hospedados; `MaxReceiveCount=3`, visibilidade 2 s, backoff 1 s) + `SqsMessagingTests` (3): pedido pago e enviado pelas duas filas (`processed_messages` com `OrderPlaced`/`OrderPaid`, webhook `Processed` pelo consumidor); **T14** reentrega do mesmo envelope → 1 pagamento; **T15** envelope `NoSuchEvent` → `receive 3/3` → DLQ. A suíte principal segue com mensageria desligada (in-process).
- Verificação manual com compose completo (Postgres + LocalStack + Aspire) e os três hosts: filas criadas pelo Worker, `Created → AwaitingPayment → DeliveryRequested → InDelivery → Delivered` em 16 s, 4 webhooks de entrega aplicados pelo Worker (0 no request da API), 0 falhas de publicação, 0 erros, 0 segredos. Build 0 warnings; format limpo; 0 vulnerabilidades.

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

## Próximas tarefas (Fase 10 — Security hardening; ler antes SECURITY.md inteiro, `authentication`, `security-scan`, `fulfillmenthub-dotnet` §8)
1. Threat model revisado com o que existe (webhooks, outbox/SQS, admin); checklist OWASP Top 10 preenchido item a item com link para código/teste (A01 broken access control — testes de acesso cruzado existentes + admin; A02; A03 injeção — EF parametrizado, `FromSqlInterpolated`; A04; A05 headers/CORS; A07 rate limit; A08 assinaturas/outbox; A09 logs).
2. Security headers (`X-Content-Type-Options`, `Referrer-Policy`, CSP mínima para o Scalar em dev), CORS explícito (nenhuma origem por padrão), limites de tamanho de request (Kestrel `MaxRequestBodySize`), `ForwardedHeaders` documentado (BL-106).
3. Redação de PII nos logs (e-mail/telefone mascarados; revisar `LoggerMessage` existentes), sem corpo de webhook/pedido em logs (já), `EnableSensitiveDataLogging` nunca fora de Development.
4. Auditoria: `dotnet list package --vulnerable` como passo documentado (já limpo), revisão de mass assignment (DTOs de request já sem campos do servidor), lockout progressivo de login (P2, decidir), rotação de chave HMAC (P2, decidir).
5. Testes: broken access control (cliente em rotas admin/operador; operador em admin; token com role adulterada), headers presentes, CORS negado, corpo > limite → 413.
6. Docs: SECURITY.md "implementado" em todas as seções, DECISIONS, BACKLOG (BL-10x), ROADMAP; fechar Gate 10.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-64…D-75)
- Fase 8: D-64 outbox própria; D-65 `Done`/`Retry`/`Failed`/requeue; D-66 estorno por dois eventos; D-67 serialização STJ; D-68 varredura mantida; D-69 rate limit de webhooks configurável. Fase 9: D-70 chave de modo `Messaging:Sqs:Enabled`; D-71 filas criadas pelo Worker; D-72 dedup na mesma transação; D-73 ponteiro de webhook + fallback; D-74 ponteiro sempre reconhecido; D-75 backoff por visibilidade.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P4 k6/NBomber · D-P6 rede AWS dev · D-P7 estado Terraform.

## Testes atuais
- 225 testes verdes (125 unit, 5 architecture, 95 integration; ~1,5 min a quente — duas fixtures com containers: Postgres e Postgres+LocalStack). Matriz TEST_STRATEGY: T1–T18 ✔ (T19 trace ponta a ponta → Fase 11; T20 caos → Fase 12).

## Infra atual
- Local: banco com 6 migrations (`OutboxMessages`, `ProcessedMessages` aplicadas); compose com Postgres + LocalStack (SQS) + Aspire Dashboard e seed; user-secrets da Api/Worker: `Database:ConnectionString`, `Jwt:SigningKey` (Api), `Seed:*Password` (Api), `Providers:Payment:*`, `Providers:Delivery:*`. Remote `origin` = https://github.com/Gabriel-PereiraL/fullfillmentHub (branch `main`); nenhuma conta/recurso AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 9 — fechado.** Critérios: mensagem envenenada → DLQ após `maxReceiveCount` ✔ (T15); consumidor recebe duplicata e não duplica efeito ✔ (T14); compose completo funciona ✔ (smoke com Postgres + LocalStack + Aspire + 3 hosts); testes com Testcontainers LocalStack ✔; BACKLOG P0 fechado ✔ (BL-084…087, 147).
**Gate 10 — Security hardening**: checklist OWASP com link para código/teste em cada item; testes de broken access control; SECURITY.md sem seções "planejado" para o que existe.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 10), com `docker compose --profile deps up -d` ativo.
