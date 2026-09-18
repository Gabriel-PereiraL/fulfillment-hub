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
