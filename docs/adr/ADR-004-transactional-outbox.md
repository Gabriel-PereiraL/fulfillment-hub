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
