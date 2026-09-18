# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 4 — Fase 3)

## Fase atual
**Fase 3 — Identity + Auth: CONCLUÍDA (Gate 3 fechado).**
Próxima fase: **Fase 4 — Orders (API + idempotência + concorrência de estoque)** (não iniciada).

## Concluído (Fase 3)
- Application: `Result<T>`/`Failure` (tipo pequeno próprio, skill §6), portas `IPasswordHasher` (com `DecoyHash`), `ITokenIssuer`, `ICurrentUser`; `LoginHandler` (falhas indistinguíveis, decoy, re-hash transparente, `LastLoginAt`, logs sem PII); `UserQueries`.
- Infrastructure/Identity: `JwtOptions` (validadas no start: chave ≥ 32 chars, lifetime 1–60 min, skew), `JwtTokenService` (`JsonWebTokenHandler`, HS256, claims `sub`/`role[]`/`customer_id`/`jti`), `IdentityPasswordHasher` (`PasswordHasher<User>` — PBKDF2-HMAC-SHA512), `AddFulfillmentHubIdentity()`; `DevelopmentSeeder` + `SeedOptions` (`dotnet run -- seed`, só Development, senhas de user-secrets).
- Api: `AddFulfillmentHubApiSecurity()` (`AddJwtBearer` configurado via `IConfigureOptions` a partir de `JwtOptions`, `MapInboundClaims=false`, `ValidAlgorithms=[HS256]`; `FallbackPolicy` autenticado + policies `CustomerOnly`/`OperatorOrAdmin`/`AdminOnly`; `HttpContextCurrentUser`; rate limiter `login` 5/min por IP), `AddValidation()` (.NET 10), endpoints `POST /api/v1/auth/login`, `GET /api/v1/me`, `GET /api/v1/users/{id}` (AdminOnly); health/OpenAPI/Scalar com `AllowAnonymous` explícito; pipeline `UseRateLimiter → UseAuthentication → UseAuthorization`.
- Testes: **143 no total** (99 unit, 5 architecture, 39 integration). Novos: `JwtTokenServiceTests`, `IdentityPasswordHasherTests`, `AuthEndpointsTests` (login ok + `/me`, `LastLoginAt`, 401 idêntico para senha errada/e-mail desconhecido/inativo, 400 ProblemDetails para payload inválido, rate limit 429 por cliente, 401 sem token com `WWW-Authenticate`, token adulterado/expirado/chave estranha → 401, health anônimo), `AuthorizationTests` (Admin 200, Customer/Operator 403, sem token 401, 404, **API recusa subir com chave curta**). Fixture com JWT de teste e middleware test-only de IP por cliente.
- Verificação manual no Kestrel com o seed: 401/401 genérico/200/`/me`/403/429 e **zero senhas ou tokens nos logs**.
- Build 0 warnings; `dotnet format` limpo; sem pacotes vulneráveis; commit local `6f28a1d`.
- Docs: SECURITY.md §2 reescrita com implementação + evidências + limitações; threat model e OWASP A01/A02/A07/A09 com status; DECISIONS D-31…D-35, D-P10 resolvida; DEVELOPMENT.md (segredos JWT/seed, comando seed).

## Em andamento
- Nada.

## Próximas tarefas (Fase 4 — Orders; ler antes `fulfillmenthub-dotnet` §5–§8, `minimal-api`, `error-handling`, `ef-core`, `efcore-patterns`, `testing`, ADR-010, DOMAIN.md §5/§12, D-P3)
1. Decidir D-P3 (taxa de entrega no pedido). Proposta registrada: cotar no `POST /orders`? **Atenção**: o provider de entrega só existe na Fase 6 — para a Fase 4 usar `DeliveryFee = null` até `MarkDeliveryRequested` (modelo atual) e reavaliar D-P3 na Fase 6. Registrar a decisão.
2. Infra: `IdempotencyRecord` (`PK(scope,key)`, `request_hash`, `status`, resposta, `expires_at`) + migration; `IIdempotencyStore`? Não — usar `IFulfillmentHubDbContext` direto; endpoint filter `IdempotencyFilter` (header `Idempotency-Key` obrigatório em POST mutável; mesma chave+hash → resposta armazenada; hash diferente → 422; em andamento → 409) — ADR-010.
3. Application/Orders: `PlaceOrderHandler` (cliente do `ICurrentUser.CustomerId`; carrega produtos tracked; `Product.Reserve`; `Order.Place`; `SaveChanges`; `DbUpdateConcurrencyException` → `Failure.Conflict`), `CancelOrderHandler` (`CustomerRequest` pelo dono; `OperatorAction` por operador/admin; libera estoque), `OrderQueries` (`GetById` com autorização por recurso → 404 para outro cliente, D-18; lista paginada keyset por `created_at`/`id`).
4. Api/Orders: `POST /api/v1/orders` (CustomerOnly), `GET /api/v1/orders/{id}`, `GET /api/v1/orders?after=…&limit=…`, `POST /api/v1/orders/{id}/cancel`; DTOs sem campos controláveis pelo servidor (anti-overposting); `ProducesProblem` 401/403/404/409/422; `DomainException`→422 já mapeada; `InsufficientStockException` → 409.
5. Catálogo mínimo para a API funcionar: `GET /api/v1/products` (autenticado) — CRUD admin (BL-029, P1) só se sobrar tempo.
6. Testes: T1–T3 (idempotência), **T4** (20 tarefas paralelas no `POST /orders` do último item → exatamente 1 sucesso, demais 409, estoque 0; comprovar que sem token de concorrência o teste falha), T16 (cliente A não vê pedido de B → 404), transições via API (cancelar), paginação.
7. Métricas `fh.orders.placed`, `fh.idempotency.hits`, `fh.stock.reservation_conflicts` (OBSERVABILITY.md) via `Meter` do projeto.
8. Atualizar DOMAIN.md/SECURITY.md (A01 autorização por recurso, overposting), PROJECT_STATE/BACKLOG/ROADMAP; fechar Gate 4.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (DECISIONS.md)
- D-31 `JsonWebTokenHandler` + claims mínimas + sem mapeamento de claims; D-32 rate limit 5/min por IP; D-33 rota inexistente → 401 para anônimo; D-34 falhas de login indistinguíveis + decoy hash; D-35 validação nativa .NET 10 confirmada; D-P10 resolvida (`/api/v1`).

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P3 taxa de entrega no pedido (Fase 4/6) · D-P4 k6/NBomber · D-P5 reconciliar antes de aplicar `paid` (Fase 5) · D-P6 rede AWS dev · D-P7 estado Terraform · D-P8 persistência do simulator.

## Testes atuais
- 143 testes verdes (`dotnet test --solution FulfillmentHub.slnx`, ~25 s a quente). Matriz TEST_STRATEGY: T5 ✔, T8-precursor (assinatura de token) ✔, T16 parcial (401/403 por papel ✔; acesso cruzado na Fase 4).

## Infra atual
- Local: compose (Postgres 17 + Aspire Dashboard); banco com migrations `InitialCreate` + `DomainModel` e dados do seed; user-secrets da Api: `Database:ConnectionString`, `Jwt:SigningKey`, `Seed:*Password`. Nenhum remote/GitHub/AWS.

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 3 — fechado.** Critérios: endpoints protegidos por padrão ✔ (`FallbackPolicy`, testes 401 em `/me`, `/users`, rota inexistente); testes de autorização por papel ✔ (200/403/401); segredo JWT fora do código e validado no start ✔ (`appsettings` com chave vazia, user-secrets, `ValidateOnStart`, teste de chave curta); `POST /auth/login` com rate limit ✔ (teste 429); SECURITY.md §2 atualizado com o desenho implementado ✔.
**Gate 4 — Orders**: cenário de race condition com teste que falha sem o token de concorrência e passa com ele; idempotência de `POST /orders` coberta (T1–T3); OpenAPI com todos os endpoints; autorização por recurso testada (T16); BACKLOG P0 de Orders fechado.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 4), com `docker compose --profile deps up -d` ativo.
