# ADR-010 — Estratégia de idempotência

**Status**: aceita · **Data**: 2026-09-18 (implementação: Fases 4–9)

## Contexto
Repetições acontecem: cliente reenvia `POST /orders` após timeout; provider reenvia webhook; nossa chamada ao provider é
repetida pelo retry; a fila entrega a mesma mensagem duas vezes. Nenhuma delas pode gerar dois pedidos, dois pagamentos,
duas entregas ou dois efeitos colaterais.

## Decisão (quatro fronteiras)

| Fronteira | Mecanismo | Chave | Comportamento |
|---|---|---|---|
| **API (entrada)** | header `Idempotency-Key` obrigatório em POSTs mutáveis; endpoint filter + tabela `idempotency_records` (`PK(scope, key)`, `request_hash`, `status`, resposta armazenada, `expires_at` 24 h) | UUID gerado pelo cliente, escopo = usuário | mesma chave + mesmo hash → resposta armazenada (mesmo status/corpo); mesma chave + hash diferente → `422`; chave `InProgress` → `409`; expirada → nova execução |
| **Webhooks (entrada)** | `INSERT` em `webhook_events` com `UNIQUE(provider, provider_event_id)` **antes** de qualquer efeito | id do evento do provider | duplicado → `200` sem reprocessar; métrica |
| **Providers (saída)** | `idempotency_key` no `POST deliveries`; `Idempotency-Key` no `POST payments`; derivadas do `OrderId` (+ tentativa de recotação) | determinística por pedido | retry/timeout não cria duplicata; `409 duplicate_delivery` → `GET` e reconciliar |
| **Consumidores (outbox/SQS)** | `processed_messages (consumer, message_id)` no mesmo commit do efeito; transições de estado idempotentes (mesmo estado = no-op) | id da mensagem | segunda entrega → no-op |

Complemento: constraints de unicidade no domínio (um pagamento ativo por pedido, uma entrega ativa por pedido) como última linha de defesa.

## Motivo
Cada fronteira tem semântica diferente; uma única "tabela mágica" não cobre todas. Persistir a chave **antes** do efeito e no mesmo
commit é o que torna a garantia real, não best-effort.

## Trade-offs
- Armazenar respostas ocupa espaço (jsonb + TTL 24 h; job de expurgo P2).
- Exigir `Idempotency-Key` complica clientes simples (aceito; é prática de APIs de pagamento/logística).
- Hash do corpo precisa de serialização canônica (usar bytes brutos do request).

## Consequências
- Testes T1–T3, T6, T9 (timeout com idempotency key), T14 na matriz de TEST_STRATEGY.md.
- OpenAPI documenta o header; ProblemDetails específicos para 409/422 de idempotência.
