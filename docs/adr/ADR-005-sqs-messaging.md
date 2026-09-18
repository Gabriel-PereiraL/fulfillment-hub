# ADR-005 — SQS como fila (LocalStack local) e critério fila vs. síncrono

**Status**: aceita · **Data**: 2026-09-18 (implementação: Fase 9)

## Contexto
Precisamos de processamento assíncrono com retry, DLQ e concorrência controlada para: publicação do outbox, ingestão de
webhooks e tarefas demoradas. A meta AWS pede SQS. Localmente é preciso algo equivalente.

## Opções
1. **SQS standard** (+ DLQ), LocalStack local, Testcontainers em testes.
2. RabbitMQ (ótimo localmente; não é o alvo AWS; mais um serviço para operar).
3. Kafka (excesso para o volume e para 1 dev).
4. Só outbox + polling no banco sem fila (funciona, mas não demonstra mensageria nem DLQ real).

## Decisão
Opção 1. Filas `fh-domain-events` e `fh-webhooks-inbound`, cada uma com DLQ (`maxReceiveCount` = 5). Consumidores no Worker
com long polling, `VisibilityTimeout` maior que o processamento, concorrência limitada e deduplicação persistida.
Antes da Fase 9, o outbox despacha in-process (Fase 8) para que o sistema funcione sem fila.

## Critério para usar fila (documentado por operação em INTEGRATIONS.md §6)
Usar fila quando: o chamador não precisa do resultado imediato; há retry longo; há efeito externo; é preciso absorver picos
(webhooks). Não usar quando: o usuário espera a resposta (cotação, login, consultas), ou quando a operação é trivial e local.

## Trade-offs
- SQS standard: at-least-once e sem ordem ⇒ idempotência e decisão por estado (já necessários pelo outbox).
- LocalStack ≠ AWS 100% (pequenas diferenças de comportamento); mitigado por testes na nuvem (Fase 16).
- Custo AWS praticamente zero para o volume.

## Consequências
- `IMessagePublisher`/consumidores usam AWS SDK for .NET; instrumentação OTel para SQS; métricas de idade/DLQ.
- Admin mostra DLQ e permite redrive.

## Implementação (Fase 9, 2026-09-18)

- **Cliente e filas**: `AddFulfillmentHubMessaging()` — `Messaging:Sqs` (`Enabled`, `ServiceUrl` do LocalStack ou vazio para a AWS, `Region`, credenciais estáticas só para LocalStack, `QueuePrefix`, `MaxReceiveCount` 5, `VisibilityTimeoutSeconds` 60, `WaitTimeSeconds` 20, `BatchSize` 10, `MaxConcurrency` 4, backoff `RetryBaseDelaySeconds`/`RetryMaxDelaySeconds`); `IAmazonSQS` singleton; `SqsQueueProvisioner` cria `fh-domain-events`/`fh-webhooks-inbound` e as DLQs `-dlq` com `RedrivePolicy` (idempotente; na AWS as filas viriam do Terraform, Fase 15) e resolve nomes → URLs. LocalStack (`localstack/localstack:4`, só `sqs`) no `docker-compose.yml`, perfil `deps`.
- **Publicação**: `IMessagePublisher` (Application) com `MessageEnvelope {id, type, payload, occurredAt, correlationId, traceParent}`; `SqsMessagePublisher` envia JSON + atributos `type`/`traceparent`; `NoOpMessagePublisher` quando `Enabled=false`. O `OutboxProcessor` publica o envelope em `fh-domain-events` (a linha da outbox vira `Processed` quando o broker aceitou) ou, com mensageria desligada, despacha in-process como na Fase 8 (D-70).
- **Consumidores** (Worker, `SqsConsumer` base): long polling, `SemaphoreSlim(MaxConcurrency)`, `DeleteMessage` só após sucesso, `ChangeMessageVisibility` com backoff exponencial+jitter em falha (a redrive policy leva à DLQ após `MaxReceiveCount` recebimentos), profundidade da DLQ a cada 30 s (`fh.queue.dlq.depth`), spans `Consumer` ligados ao `traceparent`. `DomainEventsConsumer` → `OutboxDispatcher` com dedup: `processed_messages (consumer, message_id)` inserido **na mesma transação** do efeito do handler (rollback em `Retry`; violação de PK em corrida = duplicata). `WebhooksInboundConsumer` → `WebhookEventProcessor` (evento que não está mais `Received` é reconhecido sem trabalho).
- **Webhooks**: a API persiste no inbox e publica um ponteiro `{webhookEventId, provider}` em `fh-webhooks-inbound`; sem broker (ou se o `SendMessage` falhar) processa in-process no mesmo request (log 5203). O parsing/aplicação por provider saiu dos endpoints para `IWebhookProcessor` (`PaymentWebhookProcessor`, `DeliveryWebhookProcessor`, chaveados por provider).
- **Métricas/traces**: `fh.queue.messages.processed/failed{queue,consumer}`, `fh.queue.message.age{queue}`, `fh.queue.dlq.depth{queue}`; `OpenTelemetry.Instrumentation.AWS` nas chamadas do SDK.
- **Evidência**: `SqsMessagingTests` (Testcontainers LocalStack): pedido pago e enviado atravessando as duas filas (`processed_messages` com `OrderPlaced`/`OrderPaid`); T14 mensagem reentregue → um pagamento; T15 mensagem envenenada → DLQ após 3 recebimentos (log `receive 3/3`). Smoke com compose completo (Postgres + LocalStack + Aspire) e os três hosts: `Created → … → Delivered` em 16 s; os 4 webhooks de entrega foram aplicados pelo **Worker** (0 pelo request da API); 0 erros.
- **Limitações**: sem redrive da DLQ pela Admin (BL-089, Fase 17); `processed_messages` sem expurgo (P2); FIFO não é usado (ordem decidida por estado + timestamp do provider, como já era).
