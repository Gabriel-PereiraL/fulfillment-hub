# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 3 — Fase 2)

## Fase atual
**Fase 2 — Domínio e banco: CONCLUÍDA (Gate 2 fechado).**
Próxima fase: **Fase 3 — Identity + Auth (JWT, roles)** (não iniciada).

## Concluído (Fase 2)
- Domain (`src/FulfillmentHub.Domain`): `Entity<TId>`, `AggregateRoot<TId>` (eventos), `IDomainEvent`, `IStronglyTypedId<TSelf>` + 7 IDs tipados (Guid v7), VOs `Money`, `Address`, `EmailAddress`, `PhoneNumber` (com máscara), `CourierInfo`; exceções `DomainException`, `InvalidStateTransitionException`, `InsufficientStockException`.
- Agregados: `Product` (Reserve/Release/ChangePrice/Activate/Deactivate), `Customer` + `CustomerAddress` (até 5, padrão automático), `Order` + `OrderItem` + `OrderStatusChange` (máquina de estados de DOMAIN.md §5, regras status × motivo de cancelamento, eventos `OrderPlaced/Paid/DeliveryRequested/Delivered/Cancelled`), `Payment` + `PaymentAttempt` (máquina de estados, `ApplyProviderStatus` ignora eventos stale/duplicados), `DeliveryQuote` (expiração), `Delivery` + `DeliveryEvent` (ordem canônica; disposições `Applied/Duplicate/OutOfOrder/Stale/Conflict`; cancelamento só antes de `PickupComplete`), `User` (roles fixas, ≥ 1 role).
- Persistência: `IFulfillmentHubDbContext` com 7 `DbSet`s; configurações por módulo; complex types para VOs (D-28); `StronglyTypedIdConverter<TId>` via `ConfigureConventions`; enums como string; `xmin` como concurrency token (`UseXminAsConcurrencyToken` própria); `CHECK` (estoque ≥ 0, quantidade 1..99); índices únicos parciais (`ux_payments_active_per_order`, `ux_deliveries_active_per_order`); `UNIQUE` (sku, e-mail, user_id, provider ids, delivery_id+provider_event_id); sequence `order_number_seq` (início 1000) para `orders.number`; `users.roles text[]`; snake_case (`EFCore.NamingConventions`, D-29). Migration `20260918131229_DomainModel` aplicada no banco local (recriado).
- Testes: **119 no total** (94 unit, 5 architecture, 20 integration). Novos: `OrderTests`, `ProductTests`, `PaymentTests`, `DeliveryTests`, `CustomerAndUserTests`, `ValueObjectTests`; integração `DomainPersistenceTests` (round-trips, sequence, complex types opcionais, `text[]`) e `DatabaseConstraintTests` (CHECK via SQL bruto, unique, unique parcial pagamento/entrega, **conflito `xmin` em reserva concorrente de estoque**).
- Build 0 warnings; `dotnet format` limpo; commit local `ed2e8db`.

## Em andamento
- Nada.

## Próximas tarefas (Fase 3 — Identity + Auth; ler antes `fulfillmenthub-dotnet` §8/§10, `authentication`, `minimal-api`, `error-handling`, `testing`, `docs/SECURITY.md` §2, ADR-008)
1. Pacotes: `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.x) e `Microsoft.Extensions.Identity.Core` (só `PasswordHasher<T>`); verificar versões no NuGet.
2. Infrastructure/Identity: `JwtOptions` (Issuer, Audience, SigningKey ≥ 32 bytes, ExpirationMinutes) validadas; `JwtTokenService`; `IPasswordHasher` adapter sobre `PasswordHasher<User>`.
3. Application/Identity: `LoginHandler` (e-mail + senha → token; mensagens genéricas; registra `LastLoginAt`), `ICurrentUser` (claims → `UserId`, `CustomerId?`, roles).
4. Api: `AddAuthentication().AddJwtBearer(...)`, `AddAuthorization` com `FallbackPolicy` autenticado e policies `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly`; `POST /api/v1/auth/login` (anônimo, rate limit), `GET /api/v1/me`; health/OpenAPI/Scalar liberados explicitamente.
5. Rate limiting no login (`AddRateLimiter`, por IP) — BL-103 parcial.
6. Seed de desenvolvimento por comando explícito (`dotnet run --project src/FulfillmentHub.Api -- seed`): admin, operador, cliente+customer, produtos (BL-044).
7. Testes: login ok/senha errada/usuário inativo (401 genérico), token expirado → 401, endpoint protegido sem token → 401, role errada → 403, `/me` retorna claims; `JwtOptions` inválidas → falha no start.
8. SECURITY.md §2 com o desenho implementado; PROJECT_STATE/BACKLOG/ROADMAP; fechar Gate 3.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md D-27…D-30; DOMAIN.md §12)
- `Role` enum + `text[]` (sem entidade); complex types do EF Core 10 para VOs; `EFCore.NamingConventions` para snake_case; tracking padrão do EF com `AsNoTracking()` explícito nas leituras.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P3 taxa de entrega no pedido (Fase 4) · D-P4 k6/NBomber · D-P5 reconciliar antes de aplicar `paid` (Fase 5) · D-P6 rede AWS dev · D-P7 estado Terraform · D-P8 persistência do simulator · D-P10 `/api/v1` fixo (proposta: sim; a Fase 3 já cria as rotas em `/api/v1`).

## Testes atuais
- 119 testes verdes (`dotnet test --solution FulfillmentHub.slnx`, ~17 s a quente). Matriz TEST_STRATEGY: T5 (máquinas de estado) coberto; T4 tem o precursor (conflito `xmin`) — o teste com N tarefas paralelas via API vem na Fase 4.

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco `fulfillmenthub` recriado com as 2 migrations (`InitialCreate` vazia, `DomainModel`). Nenhum remote/GitHub/AWS.
- Atenção: bancos criados antes da Fase 2 precisam ser recriados (colunas do histórico de migrations agora em snake_case — D-29).

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 2 — fechado.** Critérios: 100% das invariantes de DOMAIN.md §10 com teste ✔ (94 unit); migration aplica em banco limpo ✔ (fixture + banco local); constraints `CHECK`/`UNIQUE` provadas por testes de violação ✔; ArchitectureTests verdes ✔; `HasPendingModelChanges()` falso ✔.
**Gate 3 — Identity + Auth**: endpoints protegidos por padrão (`FallbackPolicy`); testes de autorização por papel (401/403); segredo JWT fora do código (user-secrets/env) e validado no start; `POST /auth/login` com rate limit; SECURITY.md §2 atualizado com o desenho implementado.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 3), com `docker compose --profile deps up -d` ativo.
