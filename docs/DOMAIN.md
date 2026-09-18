# DOMAIN — FulfillmentHub

Modelo de domínio inicial. Foi analisado a partir da lista sugerida (Customer, Address, Product, Order, OrderItem,
Payment, PaymentAttempt, Delivery, DeliveryQuote, DeliveryProvider, WebhookEvent, OutboxMessage, IdempotencyRecord,
AuditLog, User, Role) e ajustado: **`DeliveryProvider` não é entidade** (um campo `Provider` na entrega basta até existir um
segundo provider — YAGNI); `Address` é **value object** (owned), não entidade raiz; `WebhookEvent`, `OutboxMessage`,
`IdempotencyRecord` e `AuditLog` são **registros de infraestrutura**, persistidos pelo EF Core mas fora do modelo de domínio.

Convenções: IDs fortemente tipados (`OrderId` = `Guid` v7), `DateTimeOffset` em UTC, `Money` = `decimal` + `currency` ("BRL").
Este documento evolui a cada fase; o código é a fonte da verdade a partir da Fase 2.

## 1. Mapa de módulos e agregados

```
Catalog        Customers        Orders                 Payments                 Deliveries            Identity
─────────      ─────────        ──────                 ────────                 ──────────            ────────
Product (AR)   Customer (AR)    Order (AR)             Payment (AR)             DeliveryQuote (AR)    User (AR)
                 └ Address[]      ├ OrderItem[]          └ PaymentAttempt[]       Delivery (AR)         Role
                 (owned)          ├ Address (owned)                                └ DeliveryEvent[]
                                  └ OrderStatusChange[]                            └ CourierInfo (owned)
```

Referências **entre agregados são por ID** (nunca navegação EF entre raízes diferentes): `Order.CustomerId`,
`OrderItem.ProductId`, `Payment.OrderId`, `Delivery.OrderId`, `Delivery.QuoteId`.

## 2. Common (compartilhado)

| Tipo | Espécie | Notas |
|---|---|---|
| `Entity<TId>` | base abstrata | `Id`, igualdade por id |
| `AggregateRoot<TId>` | base abstrata | coleção de `IDomainEvent` (`Raise`, `ClearDomainEvents`) |
| `IDomainEvent` | interface | `OccurredAt` |
| `DomainException` | exceção | invariante violada; subtipos: `InvalidStateTransitionException`, `InsufficientStockException` |
| `Money` | VO (`record`) | `Amount` (decimal, 2 casas), `Currency` (ISO 4217, "BRL"); operadores `+`, `*`; nunca mistura moedas |
| `Address` | VO (`record`) | `Street`, `Number`, `Complement?`, `District`, `City`, `State`, `PostalCode`, `Country`, `Latitude?`, `Longitude?`; formatação para provider |
| `EmailAddress`, `PhoneNumber` | VO | normalização + validação básica (E.164 para telefone) |
| `OrderId`, `ProductId`, `CustomerId`, `PaymentId`, `DeliveryId`, `DeliveryQuoteId`, `UserId` | `readonly record struct (Guid)` | `New()` gera Guid v7 (`Guid.CreateVersion7()`) |

## 3. Catalog — `Product` (aggregate root)

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `ProductId` | |
| `Sku` | string(32) | único, imutável |
| `Name` | string(120) | |
| `UnitPrice` | `Money` | > 0 |
| `StockQuantity` | int | ≥ 0 (também `CHECK` no banco) |
| `IsActive` | bool | inativo não pode ser pedido |
| `CreatedAt`, `UpdatedAt` | DateTimeOffset | |
| (`xmin`) | concurrency token | otimista |

Comportamentos: `Reserve(int qty)` (lança `InsufficientStockException` se `qty > StockQuantity`), `Release(int qty)`, `Deactivate()`, `ChangePrice(Money)`.
**Cenário de concorrência #1**: duas requisições reservando as últimas unidades → uma falha com `DbUpdateConcurrencyException` (xmin) ou `CHECK` → pedido retorna 409; teste demonstra.

## 4. Customers — `Customer` (aggregate root)

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `CustomerId` | |
| `UserId` | `UserId` | único; usuário com role `Customer` |
| `Name` | string(120) | |
| `Email` | `EmailAddress` | único |
| `Phone` | `PhoneNumber` | |
| `Addresses` | `List<Address>` (owned collection) | até 5; uma marcada como padrão (`IsDefault` no owned) |
| `IsActive` | bool | |

Comportamentos: `AddAddress`, `RemoveAddress`, `SetDefaultAddress`, `Deactivate`.

## 5. Orders — `Order` (aggregate root)

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `OrderId` | |
| `Number` | string(16) | legível (`FH-2026-000123`), único, gerado por sequence do banco |
| `CustomerId` | `CustomerId` | |
| `Items` | `List<OrderItem>` | 1..50 itens; produto não repetido |
| `DeliveryAddress` | `Address` (owned) | snapshot no momento do pedido |
| `Subtotal` | `Money` | Σ itens |
| `DeliveryFee` | `Money?` | preenchida após cotação |
| `Total` | `Money` | subtotal + taxa (recalculado) |
| `Status` | `OrderStatus` | ver máquina de estados |
| `CancellationReason` | enum? | `CustomerRequest`, `PaymentFailed`, `DeliveryFailed`, `OperatorAction`, `StockUnavailable` |
| `PaymentId` | `PaymentId?` | referência |
| `DeliveryId` | `DeliveryId?` | referência |
| `StatusHistory` | `List<OrderStatusChange>` | `From`, `To`, `At`, `Reason?`, `ActorUserId?`, `CorrelationId` |
| `IdempotencyKey` | string? | chave usada na criação (auditoria) |
| `CreatedAt`, `UpdatedAt` | DateTimeOffset | |
| (`xmin`) | concurrency token | |

`OrderItem`: `Id`, `ProductId`, `Sku` (snapshot), `ProductName` (snapshot), `UnitPrice` (`Money`, snapshot), `Quantity` (1..99), `LineTotal`.

### Máquina de estados do pedido

```
            ┌──────────────────────────────────────────────────────────────┐
            │                        Cancelled (final)                     │
            └──▲──────────▲───────────────▲──────────────▲─────────────────┘
               │          │               │              │
Created ──► AwaitingPayment ──► Paid ──► DeliveryRequested ──► InDelivery ──► Delivered (final)
   │
   └──► (falha imediata de validação nunca persiste o pedido)
```

| De | Para | Gatilho | Regras |
|---|---|---|---|
| — | `Created` | `Order.Place(...)` | itens válidos, estoque reservado, evento `OrderPlaced` |
| `Created` | `AwaitingPayment` | pagamento criado no provider | `PaymentId` preenchido |
| `Created`, `AwaitingPayment` | `Cancelled` | cliente/operador, falha de pagamento (`PaymentFailed`), timeout de pagamento | libera estoque; evento `OrderCancelled` |
| `AwaitingPayment` | `Paid` | webhook/reconciliação confirma | evento `OrderPaid` |
| `Paid` | `DeliveryRequested` | entrega criada no provider | `DeliveryId`, `DeliveryFee`, `Total` recalculado |
| `Paid` | `Cancelled` | falha permanente ao cotar/criar entrega (`DeliveryFailed`) | solicita estorno → `Payment.Refund` |
| `DeliveryRequested` | `InDelivery` | webhook `pickup_complete` (ou `pickup` com courier a caminho — decisão: **`pickup_complete`**) | |
| `DeliveryRequested` | `Cancelled` | cliente/operador (antes da coleta) ou provider `canceled` | cancela entrega no provider; estorno |
| `InDelivery` | `Delivered` | webhook `delivered` | final |
| `InDelivery` | `Cancelled` | provider `returned`/`canceled` | estorno; motivo `DeliveryFailed` |

Toda transição inválida lança `InvalidStateTransitionException(from, to)`. Transições são **idempotentes** quando o
estado já é o alvo (ex.: dois webhooks `delivered` → segundo é no-op, registrado no histórico como duplicado).

Eventos de domínio de `Order`: `OrderPlaced`, `OrderPaid`, `OrderDeliveryRequested`, `OrderDelivered`, `OrderCancelled`.
(Só existem porque têm consumidores: Payments, Deliveries, Catalog-release, Operations/audit.)

## 6. Payments — `Payment` (aggregate root)

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `PaymentId` | |
| `OrderId` | `OrderId` | **único** entre pagamentos não-finalizados (`UNIQUE` parcial: `WHERE status NOT IN (Failed, Cancelled)`) |
| `Amount` | `Money` | = `Order.Total` no momento da criação (pré-taxa de entrega — ver decisão pendente D-P3) |
| `Status` | `PaymentStatus` | `Pending`, `Authorized`, `Paid`, `Failed`, `Cancelled`, `Refunded` |
| `Provider` | string | `"simulated-psp"` |
| `ProviderPaymentId` | string? | id no provider |
| `ProviderIdempotencyKey` | string | derivada de `OrderId` |
| `Attempts` | `List<PaymentAttempt>` | `Number`, `RequestedAt`, `CompletedAt?`, `Outcome` (`Succeeded/TransientFailure/PermanentFailure`), `ProviderErrorCode?`, `ProviderReference?` |
| `FailureReason` | string? | |
| `LastProviderEventAt` | DateTimeOffset? | para ordenar webhooks |
| `CreatedAt`, `UpdatedAt`, (`xmin`) | | |

```
Pending ──► Authorized ──► Paid ──► Refunded (final)
  │             │            
  ├──► Failed (final)   ◄────┘ (falha na captura)
  └──► Cancelled (final)  (pedido cancelado antes de pagar)
```

Comportamentos: `RegisterAttempt`, `MarkAuthorized(providerEventAt)`, `MarkPaid(...)`, `Fail(reason)`, `Cancel()`, `MarkRefunded()`.
Webhook mais antigo que `LastProviderEventAt` → ignorado (registrado). **Cenário de concorrência #2**: dois webhooks `paid`
simultâneos → `UNIQUE(provider, provider_event_id)` barra duplicado exato; eventos distintos com o mesmo efeito → transição idempotente + `xmin`.

## 7. Deliveries

### `DeliveryQuote` (aggregate root, imutável após criação)

| Campo | Tipo |
|---|---|
| `Id` | `DeliveryQuoteId` |
| `OrderId` | `OrderId` |
| `Provider` | string (`"uber-like-simulator"`) |
| `ProviderQuoteId` | string (`dqt_...`) |
| `Fee` | `Money` (convertido de centavos) |
| `EstimatedDropoffAt`, `DurationMinutes`, `PickupDurationMinutes` | |
| `ExpiresAt` | DateTimeOffset — `IsExpired(now)` |
| `CreatedAt` | |

### `Delivery` (aggregate root)

| Campo | Tipo | Regras |
|---|---|---|
| `Id` | `DeliveryId` | |
| `OrderId` | `OrderId` | único entre entregas ativas |
| `QuoteId` | `DeliveryQuoteId` | |
| `Provider` | string | |
| `ProviderDeliveryId` | string? (`del_...`) | único por provider |
| `ProviderIdempotencyKey` | string | derivada de `OrderId` + tentativa de recotação |
| `Status` | `DeliveryStatus` | `Requested`, `Pending`, `Pickup`, `PickupComplete`, `Dropoff`, `Delivered`, `Cancelled`, `Returned` |
| `TrackingUrl` | string? | |
| `Fee` | `Money` | |
| `Courier` | `CourierInfo?` (owned) | `Name`, `PhoneMasked`, `VehicleType`, `Latitude?`, `Longitude?` |
| `Events` | `List<DeliveryEvent>` | `ProviderEventId`, `ProviderStatus`, `OccurredAt` (provider), `ReceivedAt`, `Applied` (bool), `Note` |
| `LastProviderEventAt` | DateTimeOffset? | ordenação |
| `CreatedAt`, `UpdatedAt`, (`xmin`) | | |

Ordem canônica de status (para decidir "posterior"): `Pending(0) < Pickup(1) < PickupComplete(2) < Dropoff(3) < Delivered(4)`; `Cancelled`/`Returned` são finais e vencem tudo, exceto `Delivered` já aplicado (conflito → alerta operacional).
Regra de aplicação de webhook: aplica se `OccurredAt > LastProviderEventAt` **e** rank(status) > rank(atual) (ou final); caso contrário registra `Applied=false` com motivo (`duplicate`, `out_of_order`, `stale`).

Comportamentos: `Delivery.Request(...)`, `ConfirmCreated(providerId, trackingUrl, fee)`, `ApplyProviderEvent(evt, now)`, `Cancel(reason)`.

## 8. Identity

| `User` | `Role` |
|---|---|
| `Id`, `Email` (único), `PasswordHash` (PBKDF2 via `PasswordHasher<T>` do ASP.NET Core Identity — só a classe, sem o framework Identity completo), `IsActive`, `Roles` (M:N), `CreatedAt`, `LastLoginAt?` | `Id`, `Name` (`Customer`, `Operator`, `Admin`) |

Sem refresh token na primeira versão (JWT de curta duração + relogin); registrado como P2.

## 9. Registros de infraestrutura (não são domínio)

| Tabela | Campos principais | Uso |
|---|---|---|
| `outbox_messages` | `id`, `type`, `payload jsonb`, `occurred_at`, `processed_at?`, `attempts`, `next_attempt_at`, `last_error?`, `status`, `trace_parent` | outbox transacional |
| `webhook_events` | `id`, `provider`, `provider_event_id`, `event_type`, `payload jsonb`, `signature_valid`, `received_at`, `processed_at?`, `status`, `attempts`, `last_error?`, `correlation_id` — `UNIQUE(provider, provider_event_id)` | ingestão idempotente |
| `idempotency_records` | `key`, `scope`, `request_hash`, `status (InProgress/Completed)`, `response_status_code?`, `response_body jsonb?`, `created_at`, `expires_at` — `PK(scope, key)` | idempotência da API |
| `audit_logs` | `id`, `at`, `actor_user_id?`, `action`, `entity_type`, `entity_id`, `data jsonb`, `correlation_id` | trilha de ações operacionais/admin |
| `processed_messages` | `consumer`, `message_id`, `processed_at` — `PK(consumer, message_id)` | dedup de consumidores SQS (Fase 9) |

## 10. Invariantes resumidas (candidatas a testes de unidade)

1. Pedido sem itens, com quantidade ≤ 0, produto inativo ou repetido → não é criado.
2. `Order.Total == Subtotal + (DeliveryFee ?? 0)`; moedas iguais.
3. Reservar mais do que o estoque → `InsufficientStockException`; estoque nunca negativo.
4. Toda transição fora da tabela → `InvalidStateTransitionException`; transição para o mesmo estado é no-op idempotente.
5. Um pedido tem no máximo um pagamento ativo e uma entrega ativa.
6. Cotação expirada não pode gerar entrega.
7. Evento de entrega mais antigo que o último aplicado nunca regride o estado.
8. Cancelar pedido pago aciona estorno; cancelar pedido com entrega em curso aciona cancelamento no provider (se permitido).
9. `Money` não soma moedas diferentes.

## 11. Decisões pendentes de domínio

- **D-P3 — momento de cobrar a taxa de entrega**: hoje o pagamento é criado antes da cotação (taxa desconhecida). Opções: (a) cotar antes de criar o pedido e cobrar total com taxa; (b) cobrar produtos e ajustar/cobrar a taxa depois; (c) taxa fixa estimada. Proposta: **(a)** — `POST /orders` já cota a entrega (síncrono) e congela a taxa se a cotação for válida; recotação na criação da entrega só se expirou (diferença absorvida pela "loja"). Decidir na Fase 4.
- Refresh tokens (P2). Múltiplos endereços por cliente vs. endereço apenas no pedido (manter ambos por ora).

## 12. Implementação (Fase 2, 2026-09-18) — o código é a fonte da verdade a partir daqui

Divergências e precisões em relação às seções acima, decididas ao implementar:

| Tema | Especificado acima | Implementado | Motivo |
|---|---|---|---|
| `Role` | entidade + M:N `UserRole` | `enum Role { Customer, Operator, Admin }`; `User.Roles` persistido como `text[]` (`users.roles`) | conjunto fixo; join table seria cerimônia (D-27) |
| Endereços do cliente | owned collection de `Address` com `IsDefault` | entidade filha `CustomerAddress` (`Id`, `Label`, `Address`, `IsDefault`) em `customer_addresses`, gerida só pelo agregado | owned collection não comporta bem o VO como complex type |
| `Money`, `Address`, `CourierInfo` | owned/complex | **complex types** (EF Core 10, table splitting) — `DeliveryFee` e `Courier` são complex types opcionais (colunas nullable) | semântica de valor real (instâncias compartilhadas OK), sem identidade (D-28) |
| `Order.Number` | `string(16)` "FH-2026-000123" | `long` gerado pela sequence `order_number_seq` (início 1000); formatação "FH-…" é apresentação | geração no banco, sem round-trip extra |
| `DeliveryStatus` | `Requested, Pending, …` | idem + `DeliveryEventDisposition { Applied, Duplicate, OutOfOrder, Stale, Conflict }` no lugar de `Applied bool + Note` | classificação explícita e testável |
| `DeliveryEvent` | `Applied`, `Note` | `Disposition`; `UNIQUE(delivery_id, provider_event_id)` | idem |
| `Payment` | "webhook mais antigo é ignorado" | `ApplyProviderStatus(reported, providerEventAt, …)` retorna `false` para evento stale ou mesmo status; `StartAttempt/CompleteAttempt` com um pendente por vez | |
| Cancelamento do pedido | tabela §5 | `Order.Cancel(reason)` valida **status × motivo** (`CustomerRequest` até `DeliveryRequested`; `PaymentFailed/Timeout` até `AwaitingPayment`; `DeliveryFailed` de `Paid` a `InDelivery`; `OperatorAction` em qualquer não-final); evento `OrderCancelled` carrega `PreviousStatus` | consumidores decidem liberar estoque/estornar pelo status anterior |
| Reserva de estoque | "reserva com concorrência otimista" | `Product.Reserve/Release` + `xmin` (`IsRowVersion` em `uint "xmin"`) + `CHECK stock_quantity >= 0` | teste de integração prova o conflito (`DbUpdateConcurrencyException`) |
| IDs | `readonly record struct` | idem + `IStronglyTypedId<TSelf>` (static abstract `From`) e `StronglyTypedIdConverter<TId>` registrado em `ConfigureConventions` | um conversor para todos os IDs |
| Nomes no banco | `snake_case` | `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention`) — inclusive colunas de `__EFMigrationsHistory` | D-29 |
| Registros de infraestrutura (§9) | listados | `idempotency_records` (Fase 4), `webhook_events` (Fase 5); outbox/audit na Fase 8 | escopo da Fase 2 = agregados |
| Taxa de entrega (§3 `DeliveryFee`, D-P3) | "preenchida após cotação" | **cotada no checkout** (D-51): `Order.SetDeliveryFee` só em `Created`, entra no `Total` e no valor do pagamento; `MarkDeliveryRequested` mantém a taxa cobrada; o custo real do provider fica em `Delivery.Fee`; provider indisponível → taxa estimada configurada | o cliente sabe o que paga antes de pagar |
| Origem da coleta | não modelada | configuração `Fulfillment:Origin` (uma loja), sem agregado `Store` (D-51) | fora de escopo multi-loja |
| `Delivery` `Requested` nunca confirmada | — | é cancelada (`Delivery.Cancel`) quando uma nova tentativa com nova cotação a substitui (D-55); a última tentativa fica `Requested` com a `idempotency_key` para a próxima varredura | |

Invariantes de §10: todas cobertas por testes de unidade (`tests/FulfillmentHub.UnitTests/Domain/*`) e, onde o banco participa, por testes de integração (`tests/FulfillmentHub.IntegrationTests/Persistence/*`).
