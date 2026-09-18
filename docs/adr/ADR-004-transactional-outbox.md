# ADR-004 — Transactional outbox

**Status**: aceita · **Data**: 2026-09-18 (implementação: Fase 8)

## Contexto
Após salvar um pedido, precisamos disparar efeitos fora da transação (criar pagamento, criar entrega, publicar em fila).
Salvar no banco **e** publicar numa fila em passos separados (dual write) perde eventos se o processo cair entre os dois,
ou publica eventos de transações que deram rollback.

## Opções
1. Dual write com "melhor esforço" (publicar após `SaveChanges`) — inseguro.
2. Two-phase commit / transações distribuídas — indisponível/complexo com SQS.
3. **Transactional outbox**: evento gravado na mesma transação do agregado; publicador separado lê e publica.
4. Change Data Capture (Debezium) — infraestrutura pesada demais.

## Decisão
Opção 3. Agregados acumulam `IDomainEvent`; um `SaveChangesInterceptor` serializa para `outbox_messages` no mesmo commit.
O `OutboxPublisher` (Worker) lê em lote com `FOR UPDATE SKIP LOCKED`, despacha (Fase 8: handlers in-process; Fase 9: SQS),
marca `processed_at`; falhas → backoff exponencial com jitter, `Failed` após N (visível/reprocessável na Admin).

## Motivo
Garante at-least-once sem infraestrutura extra; é o padrão reconhecido para este problema; permite mostrar retry, DLQ lógica e observabilidade (lag).

## Trade-offs / limitações
- **At-least-once** ⇒ todo consumidor deve ser idempotente (ADR-010).
- Consistência **eventual** entre módulos: `Order` pode ficar `Created` alguns segundos antes de `AwaitingPayment`; documentado no produto.
- Polling do outbox adiciona latência (intervalo curto, ex. 500 ms) e carga leve no banco; aceitável.
- Ordem: garantida apenas por agregado dentro de um lote ordenado por `occurred_at`; consumidores não devem depender de ordem global.

## Consequências
- Nenhum efeito externo é disparado dentro de um request HTTP (exceto operações explicitamente síncronas, como cotação).
- Testes obrigatórios: perda zero com falha injetada; retry; `Failed`; reprocessamento.

## Implementação (Fase 8, 2026-09-18)

- **Captura**: `AggregateRoot<TId> : IAggregateRoot` acumula `IDomainEvent`; `OutboxInterceptor` (`SaveChangesInterceptor`, registrado no `AddDbContext`) converte os eventos dos agregados rastreados em linhas de `outbox_messages` no mesmo `SaveChanges` e limpa os eventos só depois do commit. Serialização `System.Text.Json` (`OutboxEventSerializer`: ids fortes como GUID, `Money` como `{amount, currency}`, enums como texto); `type` = nome CLR do evento, registro montado por reflexão do assembly de domínio. Colunas: `id`, `type`, `payload jsonb`, `aggregate_id`, `occurred_at`, `created_at`, `status`, `attempts`, `next_attempt_at`, `locked_until`, `processed_at`, `last_error`, `correlation_id`, `trace_parent`; índice `(status, next_attempt_at)`; `xmin`. Migration `20260918170717_OutboxMessages`.
- **Publicação**: `OutboxProcessor` (Infrastructure) — transação curta com `SELECT …, xmin FROM outbox_messages WHERE status='Pending' AND next_attempt_at <= now AND (locked_until IS NULL OR locked_until < now) ORDER BY occurred_at LIMIT n FOR UPDATE SKIP LOCKED`, lease (`Outbox:LeaseSeconds`, 60) gravada e commit; depois cada mensagem é despachada em **escopo DI próprio** para o `IOutboxHandler` registrado por chave (`AddKeyedScoped<IOutboxHandler, T>(nameof(Evento))`), com span `Outbox <type>` ligado ao `trace_parent` da request de origem. `OutboxHandling.Done` → `Processed`; `Retry`/exceção → `attempts++`, backoff exponencial com jitter (`BaseDelaySeconds` 2, `MaxDelaySeconds` 300), `Failed` após `MaxAttempts` (5). O Worker roda `OutboxPublisherService : PeriodicJob` a cada `Worker:Outbox:IntervalMs` (500).
- **Consumidores** (Application/Outbox/Handlers, todos idempotentes): `OrderPlaced` → `CreatePaymentForOrderHandler` (reusa pagamento ativo; chave de idempotência por pagamento); `OrderPaid` → `RequestDeliveryHandler` (entrega ativa → `AlreadyRequested`); `OrderCancelled` e `PaymentPaid` → `RefundPaymentHandler` (estorna só se o pedido está `Cancelled` e o pagamento `Paid`, chave `refund-{paymentId}`) — fecha BL-244 (captura tardia e cancelamento após pagamento). Resultado `Unavailable` → `Retry`; rejeições permanentes → `Done` (o handler já tratou o negócio).
- **O que saiu do request**: `POST /orders` não cria mais o pagamento (responde `Created`; D-64); `PaymentStatusApplier` não chama providers. Ficou síncrono de propósito: cotação no checkout (D-51) e cancelamento da entrega no provider dentro de `POST /orders/{id}/cancel` (§6 de INTEGRATIONS).
- **Operação**: `GET /api/v1/admin/outbox?status=Failed` e `POST /api/v1/admin/outbox/{id}/retry` (AdminOnly; `Requeue` zera tentativas). Métricas `fh.outbox.published{outcome,type}`, `fh.outbox.lag`, `fh.outbox.publish.duration`, gauges `fh.outbox.pending/failed`. Logs 7000–7002 (publisher), 7100 (requeue).
- **Rede de segurança**: `DeliveryRequestService` (varredura de `Paid` sem entrega) e as reconciliações continuam, com intervalo maior — cobrem uma mensagem parada em `Failed` sem intervenção.
- **Evidência**: `OutboxTests` — T12 (`OrderPlaced` gravado com o pedido, correlation id da request, publicado depois; falha na primeira publicação → `Pending` com `attempts=1` e `next_attempt_at` futuro, sucesso depois), T13 (3 falhas → `Failed`, listado no admin, requeue → processado), redelivery de mensagem processada → um único pagamento, cancelamento de pedido pago → estorno pela outbox. Smoke em Kestrel: `Created → AwaitingPayment → Paid → DeliveryRequested → InDelivery → Delivered` em 18 s só pela outbox + webhooks.
