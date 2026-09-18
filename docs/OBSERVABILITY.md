# OBSERVABILITY — FulfillmentHub

Objetivo: conseguir responder "o que aconteceu com o pedido X?" e "por que a API está lenta?" sem acessar o banco,
usando **logs estruturados, métricas e traces correlacionados** (OpenTelemetry). Local desde a Fase 1; AWS na Fase 16.

## 1. Stack (ADR-009)

| Sinal | Biblioteca | Local | AWS |
|---|---|---|---|
| Logs | `Microsoft.Extensions.Logging` + `LoggerMessage` (source generator) + `OpenTelemetry.Exporter.OpenTelemetryProtocol` (logs) + console JSON | Aspire Dashboard (OTLP) + stdout | CloudWatch Logs (via ADOT collector sidecar ou awslogs driver com JSON) |
| Traces | `OpenTelemetry.Instrumentation.AspNetCore`, `.Http`, `Npgsql` (nativo via `Npgsql.OpenTelemetry`), `AWSSDK` instrumentation, `ActivitySource` próprio | Aspire Dashboard | AWS X-Ray via ADOT |
| Métricas | `OpenTelemetry.Instrumentation.Runtime`, ASP.NET Core/HttpClient metrics nativas (.NET 8+ `Meter`), `Meter` próprio, Polly metering | Aspire Dashboard | CloudWatch Metrics (EMF via ADOT) + alarms |
| Health | `Microsoft.Extensions.Diagnostics.HealthChecks` (+ Npgsql, SQS custom) | `/health/live`, `/health/ready` | ALB target group health + ECS |

Por que não Serilog: o logging nativo com `LoggerMessage` já é estruturado, performático e integra com OTel sem adaptadores;
menos uma dependência para justificar. Por que Aspire Dashboard local: um container, zero configuração, mostra os três sinais.
Alternativa registrada: `grafana/otel-lgtm` (Grafana + Tempo + Prometheus + Loki) se precisar de dashboards persistentes na Fase 11.

## 2. Correlação

- `X-Correlation-Id`: aceito do cliente (validado: ≤ 64 chars, alfanumérico/`-`) ou gerado; devolvido na resposta; incluído como scope de log e como atributo do span raiz.
- `trace_id`/`span_id` (W3C `traceparent`) entram automaticamente nos logs (OTel logs) — o correlation id é conveniência para humanos e clientes; o trace id é a chave técnica.
- Propagação assíncrona: `traceparent` é salvo em `outbox_messages.trace_parent` e como message attribute no SQS; o consumidor cria o span com `ActivityContext.Parse` (link ou parent — decisão: **parent** para fluxo contínuo de um pedido, link quando o lote mistura pedidos).
- `order_id`, `payment_id`, `delivery_id`, `provider_event_id` como atributos de span e campos de log (nunca dados pessoais).

## 3. Logs

- JSON no console (container-friendly), níveis: `Information` para transições de estado e chamadas externas (resumo), `Warning` para retries/webhooks rejeitados/duplicados, `Error` para falhas que vão à outbox `Failed`/DLQ ou exceções não tratadas.
- `LoggerMessage` com `EventId` fixo por mensagem (catálogo em `Infrastructure/Telemetry/LogEvents.cs`).
- Proibido: corpo completo de request/webhook, tokens, chaves, e-mail/telefone sem máscara, stack trace em `Information`.
- Exemplo de campos: `{ "EventId": 2101, "Message": "Order {OrderId} transitioned {From}->{To}", "OrderId": "...", "From": "Paid", "To": "DeliveryRequested", "CorrelationId": "...", "TraceId": "..." }`.

## 4. Métricas (nomes seguindo convenções OTel semânticas onde existem)

| Métrica | Tipo | Dimensões | Fonte | Fase |
|---|---|---|---|---|
| `http.server.request.duration` | histograma | route, status | nativa ASP.NET Core | 1 |
| `http.client.request.duration` | histograma | `server.address`, status | nativa HttpClient | 1 |
| `fh.orders.placed` / `fh.orders.cancelled` | counter | `reason` (cancel) | Application (`OrdersMetrics`) | 4 ✔ |
| `fh.order.time_to_final` | histograma (s) | `final_status` | Worker | 11 (BL-123) |
| `fh.idempotency.hits` | counter | `outcome` (`replayed`, `conflict`, `mismatch`) | Api filter | 4 — **pendente** (só log `4100` por enquanto; adicionar contador na Fase 11) |
| `fh.stock.reservation_conflicts` | counter | `kind` (`insufficient_stock`, `concurrent_update`) | Application (`OrdersMetrics`) | 4 ✔ |
| `fh.provider.request.duration` | histograma | `provider`, `operation`, `status_code`, `attempt` | Infrastructure | 5 — coberto por `http.client.request.duration` (instrumentação OTel de `HttpClient`, tag `http.request.resend_count` = tentativa) + span `Provider <op>`; métrica própria só se a padrão não bastar (BL-246) |
| `fh.provider.retries` | counter | `provider`, `operation`, `reason` | Polly telemetry | 11 (BL-246; Polly emite `resilience.polly.strategy.events` já hoje) |
| `fh.provider.circuit_state` | gauge (0/1/2) | `provider` | Polly telemetry | 11 (BL-246) |
| `fh.webhooks.received` / `.rejected` / `.duplicates` / `.out_of_order` | counter | `provider`, `event_type` (received), `reason` (rejected), `disposition` (out_of_order: `OutOfOrder`/`Stale`) | Api/Application (`WebhooksMetrics`) | 5/7 ✔ |
| `fh.deliveries.events` | counter | `disposition` (`Applied`, `Duplicate`, `OutOfOrder`, `Stale`, `Conflict`) | Application (`DeliveryStatusApplier`) | 7 ✔ |
| `fh.payments.settled` | counter | `status` (`paid`, `failed`, `paid_after_cancellation`) | Application (`PaymentStatusApplier`) | 5 ✔ |
| `fh.deliveries.quotes` / `fh.deliveries.requested` | counter | `outcome` (`quoted`, `fallback_fee`, `rejected`, `requoted` / `created`, `adopted`, `adopted_duplicate`, `deferred`, `rejected`, `quote_expired_twice`) | Application (`DeliveriesMetrics`) | 6 ✔ |
| `fh.webhooks.processing.duration` | histograma | `provider` | Worker | 7 |
| `fh.outbox.pending` / `fh.outbox.failed` | gauge | — | `OutboxMetrics` (atualizado a cada passada do publisher) | 8 ✔ |
| `fh.outbox.published` | counter | `outcome` (`processed`, `retried`, `failed`), `type` | `OutboxProcessor` | 8 ✔ |
| `fh.outbox.lag` | histograma (s: `now - occurred_at` ao publicar) | `type` | `OutboxProcessor` | 8 ✔ |
| `fh.outbox.publish.duration` | histograma (ms por handler) | `type` | `OutboxProcessor` | 8 ✔ |
| `fh.queue.messages.processed` / `.failed` | counter | `queue`, `consumer` | Worker | 9 |
| `fh.queue.message.age` | histograma (s: `SentTimestamp` → receive) | `queue` | Worker | 9 |
| `fh.queue.dlq.depth` | gauge | `queue` | Worker (GetQueueAttributes) + CloudWatch nativo | 9/16 |
| `fh.reconciliation.corrections` | counter | `kind` (`payment_status`, `delivery_status`) | Application (`ReconcilePaymentsHandler`, executado pelo Worker) | 5 ✔ |
| `process.runtime.dotnet.*` (GC, threadpool, exceptions) | vários | — | nativa | 1 |

## 5. Traces (spans próprios)

| Span | Onde | Atributos |
|---|---|---|
| `PlaceOrder` (e demais casos de uso) | Application | `order.id`, `customer.id` (id, não nome), `order.items.count` |
| `Outbox <type>` | Infrastructure (`OutboxProcessor`, roda no Worker) | `messaging.message.id`, `outbox.attempt`; `ActivityKind.Consumer` com parent = `trace_parent` gravado na mensagem (o span do handler continua o trace da request que gerou o evento) — Fase 8 ✔ |
| `Consume <queue>` | Worker | `messaging.system=aws_sqs`, `messaging.destination.name`, `messaging.message.id` (semântica OTel messaging) |
| `Provider <op>` | Infrastructure | `peer.service=uber-like-simulator`, `provider.operation`, `provider.error.code`, `retry.attempt` — Fase 5 ✔ pagamento: `Provider CreatePayment/GetPayment/RefundPayment`; Fase 6 ✔ entrega: `Provider CreateQuote/CreateDelivery/GetDelivery/CancelDelivery` (`peer.service=uber-like-simulator`) |
| `Webhook.Ingest` | Api | `webhook.provider`, `webhook.event.type`, `webhook.duplicate` |
| DB | Npgsql automático | statement resumido (sem valores) |

## 6. Alertas (planejamento; implementação na Fase 16 com CloudWatch, simulação local na Fase 11)

| Alerta | Condição | Severidade | Ação (runbook) |
|---|---|---|---|
| API 5xx rate | > 2% em 5 min | alta | RB-1 |
| API p95 latência | > 800 ms em 5 min (`POST /orders` > 1.5 s) | média | RB-2 |
| Provider circuit aberto | `fh.provider.circuit_state == open` por > 2 min | média | RB-3 |
| Outbox lag | p95 > 60 s ou `pending` > 500 | alta | RB-4 |
| Outbox/webhook failed | `failed` > 0 | média | RB-4 |
| DLQ | `ApproximateNumberOfMessagesVisible` > 0 (DLQ) | alta | RB-5 |
| Worker sem heartbeat | sem métrica de poll por 3 min | alta | RB-6 |
| Webhooks rejeitados | > 20 em 5 min | média (possível ataque/chave errada) | RB-7 |
| Login falhas | > 100 em 5 min por IP | média | RB-8 |
| Budget AWS | > 80% do orçamento mensal | alta | destruir/pausar ambiente |

## 7. Runbook de investigação (modelo; será executado e evidenciado na Fase 11)

**RB-2 — "Cliente relata lentidão ao criar pedidos"**
1. Métrica: `http.server.request.duration` p95 por rota → `POST /orders` subiu de 300 ms para 4 s a partir de 14:02.
2. Trace: abrir um trace lento de `POST /orders` → span `Provider CreateQuote` com 3,5 s e `retry.attempt=2`, `status_code=503`.
3. Métricas do provider: `fh.provider.request.duration{provider=uber-like}` p95 subiu; `fh.provider.retries{reason=503}` cresceu; `circuit_state` = half-open.
4. Logs (filtro por `trace_id`): `Warning` "Provider returned 503 couriers_busy, retrying in 1.2 s (attempt 2/3)" — contexto: `order.id`, `correlation_id`.
5. Alerta: "Provider circuit aberto" disparou 14:05 confirmando impacto.
6. Ação: como a cotação é síncrona no `POST /orders`, a lentidão do provider vaza para o cliente → decisão registrada: reduzir timeout total da cotação para 6 s e devolver 503 ProblemDetails com `Retry-After`; avaliar cotação assíncrona (BACKLOG).
7. Pós-incidente: registrar em `docs/incidents/` (a criar na Fase 11) com linha do tempo, causa, ação, follow-ups.

**RB-4 — Outbox lag alto**: verificar worker vivo (heartbeat), `failed` (erro no `last_error`), lock preso (`SKIP LOCKED` evita), banco lento (traces Npgsql), tamanho do lote; reprocessar `Failed` pela Admin.

**RB-5 — DLQ com mensagens**: inspecionar mensagem (Admin), identificar causa (payload inválido vs. bug), corrigir, redrive.

## 8. Local (docker compose)
- `aspire-dashboard` (`mcr.microsoft.com/dotnet/aspire-dashboard`) porta 18888 (UI) / 18889 (OTLP gRPC). API/Worker/Simulator exportam via `OTEL_EXPORTER_OTLP_ENDPOINT`.
- Variáveis padrão: `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=local`.

## 9. AWS (Fase 16)
- ADOT collector como sidecar em cada task (Api, Worker) → CloudWatch Logs (log group por serviço, retenção 14 dias em dev), CloudWatch Metrics (namespace `FulfillmentHub`), X-Ray traces.
- Dashboards CloudWatch: "API", "Worker/Outbox/Queues", "Providers". Alarmes da seção 6 com SNS → e-mail.
- Custo consciente: retenção curta, métricas customizadas com poucas dimensões (evitar explosão de cardinalidade), amostragem de traces (ex.: 20% em dev, 100% de erros).
