# TEST_STRATEGY — FulfillmentHub

Objetivo: testes que **provam comportamento** e documentam as garantias do sistema (idempotência, consistência,
concorrência, resiliência). Não há meta de quantidade nem de cobertura numérica; há uma **matriz de cenários obrigatórios**.

## 1. Stack (ADR-012)

| Papel | Escolha | Justificativa |
|---|---|---|
| Framework | **xUnit v3** | padrão de fato em .NET; v3 roda no Microsoft Testing Platform, suporta `TestContext.Current.CancellationToken` |
| Asserções | **Shouldly** | legível, licença BSD; FluentAssertions v8 passou a licença comercial para uso não-OSS, evitar sinal ruim |
| Dublês | fakes escritos à mão nas portas (`IDeliveryProviderClient`, `IPaymentGatewayClient`, `IMessagePublisher`); `NSubstitute` só se um fake manual ficar caro | fakes explícitos são mais legíveis e não amarram a implementação |
| Tempo | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) | expiração de cotação, backoff, reconciliação sem `Thread.Sleep` |
| HTTP externo | `HttpMessageHandler` fake para políticas; simulator **in-process** (`WebApplicationFactory` do `ProviderSimulator`) para fluxo | testa a matriz de retry sem rede e o contrato real com rede |
| Banco | **Testcontainers** PostgreSQL 17 (um container por classe/coleção, banco limpo por teste via `Respawn` ou schema por teste) | provider in-memory esconde bugs de transação/constraint/SQL |
| Fila | Testcontainers **LocalStack** (SQS) | mesma API do SQS real |
| Host | `WebApplicationFactory<Program>` com overrides de configuração/serviços | testa pipeline completo (auth, filtros, ProblemDetails) |
| Arquitetura | **NetArchTest.Rules** | garante direção de dependências e convenções |
| E2E | `docker compose` + testes xUnit que chamam a API real (categoria `E2E`, fora do run padrão) | prova o sistema montado |
| Carga | k6 **ou** NBomber (decidir na Fase 18) | |

## 2. Pirâmide (o que cada camada prova)

### Unit (rápidos, sem I/O) — `FulfillmentHub.UnitTests`
- Domínio: invariantes, máquinas de estado (transição válida, inválida, idempotente), `Money`, regra de ordem canônica de eventos de entrega, cálculo de totais.
- Aplicação: casos de uso com fakes das portas e **DbContext real em SQLite? Não** — casos de uso que dependem de EF são testados em integração (evitar dublê de `DbSet`).
- Políticas: predicado de retry (quais status), cálculo de backoff+jitter (com semente), mapeamento de erros do provider → `Result`.
- Mapeamentos: DTO ↔ domínio, payload de webhook → comando.

### Integration — `FulfillmentHub.IntegrationTests`
- API + banco real: cada endpoint (feliz + falhas relevantes), ProblemDetails, autorização, idempotência, constraints.
- Persistência: mapeamentos EF, migrations em banco limpo, concorrência (`xmin`), `SKIP LOCKED`.
- Outbox/worker: perda zero, retry, `Failed`; consumidores SQS com LocalStack.
- Integração com simulator in-process (cliente HTTP real → simulator hospedado em outro `WebApplicationFactory`).

### Architecture — `FulfillmentHub.ArchitectureTests`
- `Domain` não referencia `Application`/`Infrastructure`/pacotes de framework; `Application` não referencia `Infrastructure`; hosts não referenciam `Domain` diretamente para lógica (podem para tipos).
- Convenções: casos de uso `sealed`, sufixo `Handler`, endpoints em classes `*Endpoints`, nenhuma classe pública em `Infrastructure` fora dos extension methods de registro (a revisar).

### E2E — `FulfillmentHub.E2ETests` (Fase 12)
- Poucos fluxos: (1) pedido → pago → entregue; (2) pedido → pagamento recusado → cancelado com estoque liberado; (3) pedido → entrega cancelada pelo provider → estorno. Com simulator em modo caótico moderado.

### Contract (Fase 12)
- DTOs do cliente vs. respostas do simulator (e vs. a spec OpenAPI de referência para os campos reproduzidos).

## 3. Matriz de cenários obrigatórios

| # | Garantia | Cenário de teste | Camada | Fase |
|---|---|---|---|---|
| T1 | Idempotência da API | mesma `Idempotency-Key` + mesmo body → 1 pedido, mesma resposta (201 → 201 com mesmo id) | integration | 4 ✔ `PlaceOrder_RepeatedWithSameKeyAndPayload_ReplaysResponse_WithoutSecondOrder` |
| T2 | Idempotência da API | mesma chave + body diferente → 422 | integration | 4 ✔ `PlaceOrder_RepeatedWithSameKeyButDifferentPayload_Returns422` |
| T3 | Idempotência da API | duas requisições concorrentes com a mesma chave → uma 201, outra 409 (ou 201 igual após conclusão) | integration | 4 ✔ `PlaceOrder_ConcurrentRequestsWithSameKey_CreateExactlyOneOrder` (10 em paralelo) |
| T4 | Concorrência de estoque | 20 tarefas paralelas comprando o último item → exatamente 1 sucesso; estoque = 0; **teste falha se o token de concorrência for removido** | integration | 4 ✔ `PlaceOrder_TwentyBuyersForTheLastUnit_ExactlyOneSucceeds`; verificado em 2026-09-18: sem `UseXminAsConcurrencyToken` no `Product` o teste falhou em 3/3 execuções ("there is only one unit in stock") |
| T5 | Máquina de estados | toda transição da tabela (válida) e uma inválida por estado | unit | 2 ✔ `OrderTests`, `PaymentTests`, `DeliveryTests` |
| T6 | Webhook duplicado | mesmo `event id` 2x → 200 ambos, um único efeito, métrica de duplicado | integration | 5 ✔ `DuplicateWebhook_IsAcknowledged_ButAppliedOnce` (1 linha em `webhook_events`, 1 transição `Paid`) |
| T7 | Webhook fora de ordem | `delivered` chega antes de `pickup` → estado vai a `Delivered` e o `pickup` posterior é registrado `Applied=false` (`stale`) | integration | 7 |
| T8 | Webhook assinatura | assinatura inválida/timestamp velho → 401, nada persistido | integration | 5 ✔ `Webhook_WithBadSignature_IsRejected_AndNothingIsPersisted` (chave errada, timestamp −10 min, sem header, corpo alterado); `…MalformedPayload_Returns400`; `Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted` (D-P5) |
| T9 | Retry correto | 429 com `Retry-After` → espera e repete; 4xx de contrato → não repete; 503 ×4 → `Unavailable` após 3 retries; timeout por tentativa → repete | unit (handler fake) | 5 ✔ `PaymentGatewayClientTests` (pipeline real de resiliência contra `ScriptedTransport`) |
| T10 | Circuit breaker | após N falhas abre; chamadas seguintes falham rápido (`BrokenCircuitException`); fecha após half-open bem-sucedido | unit/integration | 5 ✔ parcial: `CircuitBreaker_Opens_AfterSustainedFailures_AndFailsFastWithoutCallingTheProvider` (10 falhas → aberto, 0 chamadas de rede); half-open na Fase 6 |
| T11 | Cotação expirada | `expires` no passado → recotar; 2ª expiração → falha de negócio | integration (FakeTimeProvider) | 6 |
| T12 | Outbox perda zero | exceção injetada após `SaveChanges` e antes do publish → mensagem continua na outbox e é publicada pelo worker | integration | 8 |
| T13 | Outbox retry/Failed | handler lança 3x → `attempts=3`, `Failed`; reprocessar via endpoint → sucesso | integration | 8 |
| T14 | Consumidor idempotente | mesma mensagem SQS entregue 2x → um efeito | integration (LocalStack) | 9 |
| T15 | DLQ | mensagem envenenada → DLQ após `maxReceiveCount` | integration (LocalStack) | 9 |
| T16 | Autorização | cliente A `GET /orders/{id de B}` → 404 (não vaza existência); operador → 200 | integration | 4 ✔ `Customer_CannotSeeOrCancel_AnotherCustomersOrder`, `Operator_SeesEveryOrder_AndCancelsWithOperatorAction` |
| T17 | Falha permanente de pagamento | `card_declined` → `Payment Failed`, `Order Cancelled(PaymentFailed)`, estoque liberado (outbox `OrderCancelled` na Fase 8) | integration | 5 ✔ `DeclinedPayment_CancelsOrder_AndReleasesStock` (webhook real do simulator, valor sandbox `…99`) |
| T18 | Reconciliação | pagamento `Pending` há > X → job consulta provider (`paid`) → estado corrigido | integration | 5 ✔ `SilentSettlement_IsPickedUpByReconciliation` (webhook perdido, valor `…98`); `ProviderOutage_NeverFailsTheOrder_AndReconciliationRetriesTheCreation` (503 → pedido `Created`, reconciliação recria no provider); `PaymentCapturedAfterCustomerCancelled_…` (captura tardia não ressuscita pedido cancelado) |
| T19 | Trace de ponta a ponta | um pedido gera spans API→outbox→worker→provider com o mesmo `trace_id` | integration (exporter in-memory) | 11 |
| T20 | Convergência sob caos | `SIM_FAILURE_RATE=0.3`, `SIM_WEBHOOK_OUT_OF_ORDER=true` → 100% dos pedidos terminam em estado final em ≤ N s | E2E | 12 |

## 4. Regras de qualidade dos testes

- Nome: `Metodo_Cenario_ResultadoEsperado` ou frase (`PlaceOrder_WhenStockInsufficient_ReturnsConflict`). AAA com seções claras.
- Um comportamento por teste; asserção sobre **saída observável** (resposta HTTP, linhas no banco, mensagens publicadas, estado do agregado), não sobre chamadas internas.
- Proibido: teste que só verifica que um método foi chamado; teste que copia a implementação; `Thread.Sleep`; dependência de ordem entre testes; dados compartilhados mutáveis; `DateTime.Now`.
- Builders/`ObjectMother` para dados (`AnOrder().WithItems(...).Build()`), sem fixtures gigantes.
- Integração: banco limpo por teste (Respawn) ou transação revertida; containers compartilhados por coleção (`ICollectionFixture`).
- Testes de integração rodam no `dotnet test` padrão (exigem Docker); E2E e carga ficam atrás de categoria/trait.
- Falha de teste em CI é bloqueante. Flaky = bug: corrigir ou remover, nunca `retry` automático.

## 5. Como rodar (a partir da Fase 1)

```bash
dotnet test                                   # unit + architecture + integration (Docker necessário)
dotnet test --filter "Category!=E2E"          # sem E2E
dotnet test tests/FulfillmentHub.E2ETests     # com docker compose up antes
```

## 6. Evidências para o portfólio
Cada garantia da matriz terá um link no README final para o teste correspondente ("está aqui, eu implementei").
