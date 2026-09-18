# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 5 — Fase 4)

## Fase atual
**Fase 4 — Orders: CONCLUÍDA (Gate 4 fechado).**
Próxima fase: **Fase 5 — Payments (simulator + integração + webhook + reconciliação)** (não iniciada).

## Concluído (Fase 4)
- Application: `Result<T>`/`Failure` em uso; `AddFulfillmentHubApplication()` (handlers/queries/métricas); `PlaceOrderHandler` (validações, reserva de estoque com `xmin` + retry limitado 3×, `ChangeTracker.Clear()` entre tentativas, `Failure.Conflict` em estoque insuficiente/conflito persistente), `CancelOrderHandler` (dono ou operador; `OperatorAction` vs `CustomerRequest`; libera estoque na mesma transação; pedido alheio → 404), `OrderQueries` (visibilidade por papel; keyset por `Number` com cursor base64url; `pageSize` 1–100), `ProductQueries`, `OrdersMetrics` (`fh.orders.placed/cancelled`, `fh.stock.reservation_conflicts{kind}`), DTOs (`OrderDto`, `OrderSummaryDto`, `OrderPage`, `MoneyDto`, `AddressDto`).
- Infrastructure: `IdempotencyRecord` + `IdempotencyStore` (escopo DI próprio; PK `(scope,key)`; TTL 24 h; `Begin/Complete/Release`) + migration `20260918140549_IdempotencyRecords`; todas as chaves client-side com `ValueGeneratedNever()` (D-39).
- Api: `IdempotencyFilter<TRequest,TResponse>` (header obrigatório, fingerprint JSON canônico, replay com status/corpo/`Location`/`Idempotent-Replayed`, 409 em andamento, 422 payload divergente, chave liberada em 5xx/exceção), `FailureResults.ToProblem()` (mapa `FailureKind` → HTTP), endpoints `POST /api/v1/orders` (CustomerOnly), `GET /api/v1/orders/{id}`, `GET /api/v1/orders?cursor&pageSize&customerId`, `POST /api/v1/orders/{id}/cancel`, `GET /api/v1/products`; DTOs de request sem campos controlados pelo servidor.
- Domain: `Order.CanCancel(reason)`; coleções históricas ordenadas pelo agregado (D-40).
- Testes: **160 no total** (99 unit, 5 architecture, 56 integration); 17 novos em `Orders/`: T1 replay, T2 422, T3 mesma chave concorrente (10×), **T4 20 compradores → 1 vence, estoque 0** (verificado falhar sem `xmin`, 3/3), overposting ignorado, quantidade inválida → 400, produto inexistente → 404, operador → 403, sem chave → 400, T16 acesso cruzado (GET/cancel/lista) → 404 sem liberar estoque, operador cancela com `OperatorAction`, cancelamento idempotente, cancelamento após coleta → 409, paginação com cursor + cursor inválido → 400, produtos.
- Verificação manual no Kestrel com o seed: 201 + `Location`, replay, 422, lista, cancelamento; 0 tokens no log. Build 0 warnings; format limpo; sem pacotes vulneráveis; migration aplicada no banco local. Commit `f69c0a8`.

## Em andamento
- Nada.

## Próximas tarefas (Fase 5 — Payments; ler antes `fulfillmenthub-dotnet` §9/§11, `httpclient-factory`, `resilience`, `minimal-api`, `testing`, INTEGRATIONS.md §3–§5, ADR-003, D-P5)
1. ProviderSimulator `/payments/v1`: `POST /payments` (header `Idempotency-Key`, `amount` em centavos, `currency`, `order_reference`, `scenario?`) → `pay_…` `pending/authorized`; `GET /payments/{id}`; `POST /payments/{id}/refunds`; transição automática para `paid`/`failed` após `SIM_PAYMENT_SETTLE_MS`; webhook `payment.status_changed` assinado (`X-Signature` HMAC-SHA256 hex, `X-Timestamp`) para a URL configurada; cenários `SIM_PAYMENT_APPROVAL_RATE`, `SIM_LATENCY_MS`, `SIM_FAILURE_RATE`, `SIM_WEBHOOK_DUPLICATE_RATE`, `SIM_WEBHOOK_DELAY_MS`. Estado em memória (D-P8 → decidir: memória).
2. Application/Payments: porta `IPaymentGatewayClient` (`CreateAsync`, `GetAsync`, `RefundAsync`) com DTOs próprios; `CreatePaymentForOrderHandler` (disparado **in-process** logo após `PlaceOrder` nesta fase — via outbox na Fase 8): `Payment.Create` + `StartAttempt` + chamada + `CompleteAttempt`; `Order.MarkAwaitingPayment`; falha transitória → retry pela pipeline; permanente → `Payment.Fail`, `Order.Cancel(PaymentFailed)`, libera estoque.
3. Infrastructure/Providers/Payment: typed client `SimulatedPaymentGatewayClient` com `AddResilienceHandler` (timeout total 15 s, retry 3× exponencial+jitter só em 408/429/5xx/timeout, CB), `Idempotency-Key = Payment.ProviderIdempotencyKey`, mapeamento de erros → `Failure`; Options `Providers:Payment:*` validadas; `ActivitySource` + métricas `fh.provider.request.duration`.
4. Webhook: tabela `webhook_events` (`UNIQUE(provider, provider_event_id)`, payload jsonb, status) + migration; `POST /api/v1/webhooks/payments` (anônimo + assinatura HMAC em tempo constante + tolerância 5 min + limite de corpo 64 KB + rate limit); persistir → processar in-process (`ApplyPaymentWebhookHandler`: `Payment.ApplyProviderStatus`, `Order.MarkAsPaid` / cancelamento) → `200` rápido; duplicado → 200 no-op.
5. Decidir D-P5 (reconciliar com `GET` antes de aplicar `paid`) — proposta: sim.
6. Worker: `PaymentReconciliationService` (`Pending` há > X min → `GET` no provider → aplica status); métrica `fh.reconciliation.corrections`.
7. Testes: T6 (webhook duplicado), T8 (assinatura inválida → 401), T17 (`card_declined` → cancelado + estoque liberado), T18 (reconciliação), matriz de retry com `HttpMessageHandler` fake (429 `Retry-After`, 400 sem retry, 503×3), fluxo pedido→pago com o simulator in-process (`WebApplicationFactory` do simulator).
8. Docs: INTEGRATIONS.md §3 (contrato final do simulator), SECURITY.md (webhook forjado — evidências), OBSERVABILITY (métricas de provider/webhook), README do simulator; PROJECT_STATE/BACKLOG/ROADMAP; fechar Gate 5.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-36…D-41; ADR-010 "Implementação")
- D-36 taxa de entrega nula até a Fase 6 (D-P3 parcial); D-37 fingerprint/replay de idempotência; D-38 keyset por `Number`; D-39 `ValueGeneratedNever` em chaves client-side; D-40 ordenação das coleções históricas no agregado; D-41 retry limitado na reserva de estoque.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P3 cotação síncrona vs. taxa estimada (Fase 6) · D-P4 k6/NBomber · D-P5 reconciliar antes de aplicar `paid` (Fase 5) · D-P6 rede AWS dev · D-P7 estado Terraform · D-P8 persistência do simulator (Fase 5/6).

## Testes atuais
- 160 testes verdes, estável em 3 execuções seguidas (~20 s a quente). Matriz TEST_STRATEGY: T1–T5 ✔, T16 ✔ (evidências anotadas na tabela).

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco com 3 migrations (`InitialCreate`, `DomainModel`, `IdempotencyRecords`) e seed; user-secrets da Api: `Database:ConnectionString`, `Jwt:SigningKey`, `Seed:*Password`. Nenhum remote/GitHub/AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet` (§3 atualizada: DTOs pequenos podem compartilhar `<Módulo>Dtos.cs`).

## Próximo gate
**Gate 4 — fechado.** Critérios: race condition com teste que falha sem o token e passa com ele ✔ (T4, 3/3 sem `xmin` falhou); idempotência T1–T3 ✔; OpenAPI com todos os endpoints ✔ (metadados `Produces*`/`WithName`); autorização por recurso T16 ✔; BACKLOG P0 de Orders fechado ✔ (BL-009, 024, 025, 026, 031, 105, 142, 143).
**Gate 5 — Payments**: fluxo pedido→pago passa em integração com o simulator in-process; webhook duplicado não gera efeito duplo (T6); assinatura inválida → 401 (T8); falha permanente cancela pedido e libera estoque (T17); reconciliação corrige pendente (T18); matriz de retry testada.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 5), com `docker compose --profile deps up -d` ativo.
