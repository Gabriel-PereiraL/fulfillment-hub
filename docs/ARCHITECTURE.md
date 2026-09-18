# ARCHITECTURE — FulfillmentHub

## 1. Estilo: monólito modular, três processos

Um único produto, uma única solução, um único banco. Três processos porque têm ciclos de vida e perfis de escala
diferentes — não porque "microserviço é bonito":

| Processo | Projeto | Por que é separado |
|---|---|---|
| **API** | `FulfillmentHub.Api` | atende HTTP (clientes, admin, webhooks); escala por request; deve responder rápido |
| **Worker** | `FulfillmentHub.Worker` | outbox publisher, consumidores SQS, reconciliação; escala por backlog; pode reiniciar sem derrubar a API |
| **Provider Simulator** | `FulfillmentHub.ProviderSimulator` | representa sistemas **externos**; precisa ser um processo separado para a integração HTTP ser real (rede, timeout, falhas) |

Tudo compartilha `Domain`, `Application` e `Infrastructure` (exceto o Simulator, que é propositalmente independente
para não "vazar" conhecimento interno do FulfillmentHub para dentro do "provider").

O que **não** existe e por quê (ADR-001): microserviços (custo operacional sem benefício em um time de 1), Kubernetes
(ECS Fargate resolve), Kafka (SQS resolve com muito menos operação), service mesh, API gateway dedicado, CQRS com
bancos separados, event sourcing.

## 2. Solução e projetos

```
FulfillmentHub.sln
├── Directory.Build.props            # Nullable, ImplicitUsings, TreatWarningsAsErrors, analyzers, LangVersion
├── Directory.Packages.props         # Central Package Management
├── .editorconfig
├── docker-compose.yml               # postgres, localstack (SQS), aspire-dashboard (OTLP), simulator
├── src/
│   ├── FulfillmentHub.Domain/           # entidades, VOs, eventos de domínio, exceções de domínio. Sem NuGet de framework.
│   ├── FulfillmentHub.Application/      # casos de uso, DTOs, portas (interfaces p/ externos), IFulfillmentHubDbContext, Result
│   ├── FulfillmentHub.Infrastructure/   # EF Core + Npgsql, migrations, outbox, SQS, HttpClients dos providers, auth (JWT/hash), telemetria
│   ├── FulfillmentHub.Api/              # Minimal APIs, filtros (idempotência, validação), ProblemDetails, OpenAPI, composition root
│   ├── FulfillmentHub.Worker/           # BackgroundServices: OutboxPublisher, consumers, reconciliation
│   ├── FulfillmentHub.ProviderSimulator/# Minimal APIs que imitam providers (delivery "Uber-like" e pagamento) + envio de webhooks
│   └── FulfillmentHub.Admin/            # Blazor Web App (Fase 17) — usa Application/Infrastructure diretamente
└── tests/
    ├── FulfillmentHub.UnitTests/        # domínio, casos de uso com fakes, políticas de retry, mapeamentos
    ├── FulfillmentHub.IntegrationTests/ # WebApplicationFactory + Testcontainers (PostgreSQL, LocalStack) + simulator in-process
    ├── FulfillmentHub.ArchitectureTests/# NetArchTest: direção de dependências, convenções
    └── FulfillmentHub.E2ETests/         # poucos fluxos completos com compose (Fase 12)
```

### Direção de dependências (validada por ArchitectureTests)

```
Api ──────► Application ──► Domain
 │              ▲
 ▼              │
Infrastructure ─┘  (Infrastructure implementa as portas de Application e o DbContext)
Worker ───► Application, Infrastructure
Admin ────► Application, Infrastructure
ProviderSimulator ──► (nada do FulfillmentHub)
```

Regras:
- `Domain` não referencia nenhum pacote além da BCL.
- `Application` referencia `Domain` e o pacote `Microsoft.EntityFrameworkCore` (para `DbSet<T>`/LINQ via `IFulfillmentHubDbContext`).
  É uma dependência assumida e documentada (ADR-006): o custo de abstrair o EF Core é maior que o benefício.
- `Infrastructure` referencia `Application` (implementa portas) — nunca o contrário.
- Hosts (`Api`, `Worker`, `Admin`) são composition roots: registram módulos via extension methods.

## 3. Organização por módulo

Dentro de cada projeto, pastas por **módulo de domínio** (bounded contexts leves), não por tipo técnico:

```
Domain/
  Common/        Entity, AggregateRoot, IDomainEvent, DomainException, Money, Address, strongly-typed ids
  Catalog/       Product
  Customers/     Customer (+ endereços salvos)
  Orders/        Order, OrderItem, OrderStatus, OrderStatusChange, eventos (OrderPlaced, OrderPaid, OrderCancelled...)
  Payments/      Payment, PaymentAttempt, PaymentStatus, eventos
  Deliveries/    Delivery, DeliveryQuote, DeliveryEvent, DeliveryStatus, eventos
  Identity/      User, Role

Application/
  Common/        Result, Error, IFulfillmentHubDbContext, IClock? (não: usar TimeProvider), IIdempotencyStore
  Orders/        PlaceOrderHandler, CancelOrderHandler, OrderQueries, DTOs
  Payments/      CreatePaymentForOrderHandler, ApplyPaymentWebhookHandler, ReconcilePaymentsHandler, IPaymentGatewayClient
  Deliveries/    RequestDeliveryHandler, ApplyDeliveryWebhookHandler, CancelDeliveryHandler, IDeliveryProviderClient
  Catalog/, Customers/, Identity/, Operations/

Infrastructure/
  Persistence/   FulfillmentHubDbContext, Configurations/<Módulo>/, Migrations/, Interceptors (outbox, audit), conversores
  Outbox/        OutboxMessage, OutboxProcessor
  Messaging/     SQS publisher/consumer, LocalStack config
  Providers/     DeliveryProvider/UberLikeDeliveryClient (+ contratos), PaymentGateway/SimulatedPaymentGatewayClient
  Identity/      PasswordHasher, JwtTokenService
  Telemetry/     ActivitySources, Meters, extensões de registro OTel
  Idempotency/   IdempotencyRecord store
```

Comunicação **entre módulos** dentro do monólito:
- Preferencialmente por **eventos via outbox** (Orders → Payments → Deliveries), o que já é o fluxo do produto.
- Chamadas diretas a casos de uso de outro módulo são permitidas quando síncronas por natureza (ex.: `PlaceOrder`
  consulta `Catalog` para preço/estoque no mesmo commit). Não criar "anti-corruption layer" entre módulos internos.
- Um único `DbContext` e um único schema (tabelas com prefixo por módulo não é necessário; nomes claros bastam).

## 4. Padrões adotados e recusados

| Tema | Adotado | Recusado (e por quê) |
|---|---|---|
| Acesso a dados | `IFulfillmentHubDbContext` (DbSets + SaveChanges) direto nos casos de uso | repository genérico/UoW (duplicam o EF Core), specification pattern |
| Orquestração de casos de uso | classes explícitas por caso de uso, DI direto | MediatR/Mediator (dispatcher sem problema que o justifique; MediatR virou comercial em 2025) |
| API | Minimal APIs, `MapGroup` por módulo, `TypedResults`, registro explícito | controllers (nenhum ganho aqui), auto-discovery por reflexão |
| Erros | ProblemDetails (RFC 9457) + `IExceptionHandler`; `Result` pequeno para falhas esperadas; `DomainException` para invariantes | Result em todo método; exceptions para fluxo |
| Validação | validação nativa de Minimal APIs (.NET 10, DataAnnotations) + invariantes de domínio | FluentValidation (dependência sem necessidade) |
| Mapeamento | métodos explícitos (`ToResponse()`), projeções LINQ | AutoMapper/Mapster |
| Eventos | domain events → outbox → SQS → worker (Fases 8–9 ✔; in-process quando `Messaging:Sqs:Enabled=false`) | handlers in-process com efeitos externos (dual write) |
| Consistência | outbox transacional, consumidores idempotentes, constraints no banco | 2PC, sagas com orquestrador dedicado |
| Concorrência | otimista (`xmin`) + constraints (`CHECK`, `UNIQUE`) | locks pessimistas por padrão (só onde justificado: `SKIP LOCKED` no outbox) |
| HTTP externo | typed `HttpClient` + `Microsoft.Extensions.Http.Resilience` | Polly v7 manual, retry cego |
| Logging | `Microsoft.Extensions.Logging` + `LoggerMessage` + OpenTelemetry | Serilog (bom, mas dispensável aqui) |
| Tempo | `TimeProvider` injetado | `DateTime.Now` |
| Configuração | Options tipadas validadas no start | `IConfiguration["x"]` espalhado |

## 5. Componentes transversais

### 5.1 Idempotência (ADR-010)
- **API**: header `Idempotency-Key` obrigatório em `POST /orders` (e demais POSTs mutáveis). Filtro de endpoint:
  chave + escopo (usuário) + hash do body → `IdempotencyRecord`. Mesma chave + mesmo hash → devolve resposta armazenada;
  mesma chave + hash diferente → `422`; chave em andamento → `409`. Expira em 24h.
- **Webhooks**: `UNIQUE (provider, provider_event_id)` em `WebhookEvent`; inserção antes de qualquer efeito; duplicata → `200` sem reprocessar.
- **Saída**: `idempotency_key` enviado ao provider de entrega na criação; chave derivada do `OrderId` (retry seguro).
- **Consumidores** (Fase 9 ✔): `processed_messages (consumer, message_id)` inserida na mesma transação do efeito (`OutboxDispatcher`); o consumidor de webhooks usa o próprio status do `WebhookEvent`.

### 5.2 Outbox transacional (ADR-004)
- Agregados acumulam `IDomainEvent`; um `SaveChangesInterceptor` converte em `OutboxMessage` (tipo, payload JSON, `occurred_at`, trace context) **no mesmo commit**.
- `OutboxProcessor`/`OutboxPublisherService` (Worker) lê em lote com `FOR UPDATE SKIP LOCKED` + lease, publica no SQS (Fase 9 ✔; handlers in-process quando a mensageria está desligada), marca `processed_at`;
  falha → `attempts++`, `next_attempt_at` com backoff exponencial + jitter; após N → `Failed` (visível na Admin, reprocessável).
- Garantia: **at-least-once**. Consequência: todo consumidor é idempotente.

### 5.3 Mensageria (ADR-005)
- Filas SQS standard: `fh-domain-events` (saída do outbox), `fh-webhooks-inbound` (webhooks aceitos pela API para processamento assíncrono), cada uma com DLQ.
- Local: **LocalStack** no compose (perfil `deps`). Testes: Testcontainers LocalStack (`SqsMessagingTests`). Implementação: ADR-005 "Implementação" (Fase 9 ✔): `SqsConsumer` base (long polling, concorrência limitada, delete após sucesso, backoff por visibilidade), `DomainEventsConsumer`, `WebhooksInboundConsumer`, `SqsQueueProvisioner` (filas + DLQ com redrive criadas no start).
- Quando **não** usar fila: cotação de entrega (síncrona, o usuário espera a taxa), login, consultas. Documentado por caso em INTEGRATIONS.md.

### 5.4 Resiliência
- Pipelines por provider (`AddResilienceHandler`): timeout total, retry (exp. + jitter) com predicado por status (INTEGRATIONS.md §retry), circuit breaker, timeout por tentativa.
- Bulkhead: limite de concorrência nos consumidores (`SemaphoreSlim`), não por provider inicialmente.
- Rate limiting de entrada na API (`AddRateLimiter`): por usuário/IP em endpoints públicos e webhooks.

### 5.5 Segurança (SECURITY.md)
- JWT bearer emitido pela própria API; roles/policies; `FallbackPolicy` = autenticado; webhooks autenticados por assinatura HMAC + tolerância de timestamp.
- Secrets: user-secrets/env local; AWS Secrets Manager em nuvem.

### 5.6 Observabilidade (OBSERVABILITY.md)
- OpenTelemetry: traces (ASP.NET Core, HttpClient, Npgsql, AWS SDK, spans próprios), métricas (runtime, ASP.NET, próprias: outbox lag, retries, webhook failures, provider latency), logs correlacionados por `trace_id`.
- Local: Aspire Dashboard (container) recebendo OTLP. AWS: ADOT collector sidecar → CloudWatch (logs/metrics) e X-Ray (traces).

## 6. Fluxos críticos (sequências)

### 6.1 PlaceOrder (síncrono, transacional)
1. Filtro de idempotência (chave/escopo/hash).
2. Validação de forma (DataAnnotations).
3. Handler: carrega cliente; carrega produtos (tracked); `Order.Place(...)` (invariantes); `product.Reserve(qty)` (lança se insuficiente → `Result` de falha);
   adiciona `OrderPlaced` ao agregado.
4. `SaveChangesAsync`: interceptor grava outbox; `xmin` do produto detecta corrida → `DbUpdateConcurrencyException` → o handler retorna `409 conflict` (cliente repete com a mesma chave).
5. `201 Created` com `Location`; resposta armazenada no `IdempotencyRecord`.

### 6.2 Pagamento (assíncrono)
1. Worker consome `OrderPlaced` → `CreatePaymentForOrderHandler`: cria `Payment(Pending)` + `PaymentAttempt#1`, chama `IPaymentGatewayClient.CreateAsync` (idempotente por `OrderId`).
2. Provider responde `pending/authorized`; salva; `Order → AwaitingPayment`.
3. Webhook `payment.status_changed` → API valida assinatura, insere `WebhookEvent` (dedup), enfileira, responde `200` em < 200 ms.
4. Worker aplica: `Payment.MarkPaid(...)`, `Order.MarkAsPaid(...)`, outbox `OrderPaid`. Falha → `Payment.Fail`, `Order.Cancel(PaymentFailed)`, estoque liberado.
5. Reconciliação: `Pending` há > X min → `GET` no provider.

### 6.3 Entrega (assíncrono + resiliente)
1. `OrderPaid` → `RequestDeliveryHandler`: `POST delivery_quotes` (retry por matriz) → salva `DeliveryQuote` (com `expires`).
2. `POST deliveries` com `quote_id` + `idempotency_key = order id` → salva `Delivery(Pending)`; `Order → DeliveryRequested`. `409 duplicate_delivery` → `GET` e reconcilia. `expired_quote` → recotar (máx. 2x).
3. Webhooks `event.delivery_status` → dedup → fila → `ApplyDeliveryWebhookHandler`: aplica transição se o status for "posterior" ao atual (ordem canônica) ou registra como evento histórico fora de ordem sem regredir estado.
4. `delivered` → `Order → Delivered`. `canceled/returned` → regras de cancelamento/estorno.

## 7. Decisões estruturais registradas
ADR-001 (monólito modular), ADR-004 (outbox), ADR-005 (SQS), ADR-006 (sem repository/MediatR), ADR-007 (Minimal APIs), ADR-010 (idempotência).
Índice completo em `DECISIONS.md` e `adr/`.
