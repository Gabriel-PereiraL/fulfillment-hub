# DECISIONS — registro de decisões

Decisões importantes do projeto: contexto, opções, decisão, motivo, trade-offs, consequência. As estruturais têm ADR
em `adr/`; as menores ficam só aqui. Não existe "melhor arquitetura universal": cada decisão é fundamentada no
contexto (1 desenvolvedor, portfólio C#/.NET, monólito modular, custo consciente, honestidade).

## Índice de ADRs

| ADR | Título | Status |
|---|---|---|
| [ADR-001](adr/ADR-001-modular-monolith.md) | Monólito modular com 3 processos (Api, Worker, Simulator) | aceita |
| [ADR-002](adr/ADR-002-postgresql-efcore.md) | PostgreSQL + EF Core (Npgsql) | aceita |
| [ADR-003](adr/ADR-003-provider-simulators.md) | Providers externos simulados (delivery "Uber-like" e pagamento) | aceita |
| [ADR-004](adr/ADR-004-transactional-outbox.md) | Transactional outbox para eventos de domínio | aceita |
| [ADR-005](adr/ADR-005-sqs-messaging.md) | SQS (LocalStack local) como fila; quando usar fila vs. síncrono | aceita |
| [ADR-006](adr/ADR-006-no-repository-no-mediatr.md) | DbContext direto via interface; sem repository/UoW genérico; sem MediatR | aceita |
| [ADR-007](adr/ADR-007-minimal-apis-problemdetails-validation.md) | Minimal APIs + ProblemDetails + validação nativa + OpenAPI/Scalar | aceita |
| [ADR-008](adr/ADR-008-authentication-jwt.md) | Usuários próprios + JWT bearer + policies | aceita |
| [ADR-009](adr/ADR-009-observability-opentelemetry.md) | OpenTelemetry com logging nativo (sem Serilog); Aspire Dashboard local; CloudWatch/X-Ray na AWS | aceita |
| [ADR-010](adr/ADR-010-idempotency.md) | Estratégia de idempotência (API, webhooks, saída, consumidores) | aceita |
| [ADR-011](adr/ADR-011-aws-ecs-fargate-terraform.md) | AWS: ECS Fargate + RDS + SQS + Secrets Manager via Terraform, com trade-offs de custo | aceita |
| [ADR-012](adr/ADR-012-testing-stack.md) | xUnit v3 + Shouldly + Testcontainers + WebApplicationFactory + NetArchTest | aceita |
| [ADR-013](adr/ADR-013-dotnet-10-lts.md) | .NET 10 LTS / C# 14 | aceita |

## Decisões menores (sem ADR)

| ID | Decisão | Motivo / trade-off | Data |
|---|---|---|---|
| D-01 | Nome **FulfillmentHub** mantido; namespaces `FulfillmentHub.*` | descritivo, profissional; revisão possível na Fase 19 sem custo técnico | 2026-09-18 |
| D-02 | Documentação em **pt-BR**, código/identificadores/commits em **inglês** | usuário e avaliadores iniciais são brasileiros; código em inglês é padrão. Tradução para inglês é decisão pendente (D-P1) | 2026-09-18 |
| D-03 | Auth (Fase 3) **antes** de Orders (Fase 4) | pedido precisa de principal; retrofit de auth gera retrabalho | 2026-09-18 |
| D-04 | Docker Compose de dependências e observabilidade básica já na Fase 1 | banco local é pré-requisito; observabilidade não é pós-projeto | 2026-09-18 |
| D-05 | Um único processo `ProviderSimulator` hospeda os dois providers (rotas `/delivery/v1` e `/payments/v1`) | menos processos para rodar; separação por rota basta; pode ser dividido se crescer | 2026-09-18 |
| D-06 | `DeliveryProvider` **não** é entidade; campo `Provider` string na entrega | YAGNI até existir segundo provider (BL-069) | 2026-09-18 |
| D-07 | Value objects compostos como `record` (class) complex/owned types; IDs como `readonly record struct` + conversor | compatibilidade com EF Core; structs só onde simples | 2026-09-18 |
| D-08 | `TimeProvider` da BCL em vez de `IClock` próprio | já existe no .NET 8+, com `FakeTimeProvider` oficial para testes | 2026-09-18 |
| D-09 | Sem AutoMapper/Mapster; mapeamento explícito | reflexão desnecessária; projeções LINQ cobrem leitura | 2026-09-18 |
| D-10 | Sem FluentValidation; validação nativa de Minimal APIs (.NET 10) + invariantes de domínio | uma dependência a menos; regras de negócio não vivem em validators de request | 2026-09-18 |
| D-11 | Shouldly em vez de FluentAssertions | FluentAssertions v8 tem licença comercial fora de OSS/uso pessoal; Shouldly é BSD e legível | 2026-09-18 |
| D-12 | Aspire Dashboard como backend OTLP local (não a orquestração Aspire) | zero configuração; Compose continua sendo a orquestração | 2026-09-18 |
| D-13 | LocalStack para SQS local (não ElasticMQ) | mais próximo da AWS real, suporte a DLQ/atributos; comunidade | 2026-09-18 |
| D-14 | Guid v7 (`Guid.CreateVersion7()`) para IDs | ordenável no tempo, bom para índices B-tree do Postgres | 2026-09-18 |
| D-15 | `xmin` do PostgreSQL como token de concorrência otimista | nativo, sem coluna extra; suportado pelo Npgsql EF provider | 2026-09-18 |
| D-16 | Webhook de entrega reproduz `event.delivery_status` (DaaS atual), não o legado `dapi.status_changed` | é o contrato atual da Direct API; o legado só é citado | 2026-09-18 |
| D-17 | Order passa a `InDelivery` em `pickup_complete` (não em `pickup`) | "pickup" significa courier a caminho; a posse muda em `pickup_complete` | 2026-09-18 |
| D-18 | 404 (não 403) quando cliente acessa pedido de outro | não revelar existência do recurso | 2026-09-18 |
| D-19 | Skills privadas em `.claude/skills/` (mesmo layout do Serraf-Pessoas) + `.ai/` para memória; tudo no `.gitignore` | consistência com o outro projeto; Claude Code carrega `.claude/skills` automaticamente | 2026-09-18 |
| D-20 | `github-workflow-enforcer` do Serraf-Pessoas não copiada | obriga push; viola invariante | 2026-09-18 |
| D-21 | Base das skills C#: kit do Mukesh + complementos Aaron/Microsoft + skill própria curada com revisão crítica | melhor aderência a .NET 10 e pragmatismo; descartados kits dogmáticos (Clean Architecture + Repository + MediatR) | 2026-09-18 |
| D-22 | Testes rodam no **Microsoft.Testing.Platform** (`global.json` → `"test": {"runner": "Microsoft.Testing.Platform"}`, `UseMicrosoftTestingPlatformRunner`, sem `Microsoft.NET.Test.Sdk`); comandos `dotnet test --solution/--project` | o .NET 10 SDK não roda xunit.v3 no modo VSTest; MTP é o caminho suportado | 2026-09-18 |
| D-23 | Health check do banco via `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (`AddDbContextCheck`) | pacote Microsoft alinhado ao EF Core 10; evita `AspNetCore.HealthChecks.NpgSql` (terceiro, 9.0.0) | 2026-09-18 |
| D-24 | Exportador OTLP registrado **somente** quando `OTEL_EXPORTER_OTLP_ENDPOINT` está configurado | testes e ambientes sem coletor não tentam exportar; configuração (não código) decide o destino | 2026-09-18 |
| D-25 | `Microsoft.EntityFrameworkCore.Design` referenciado no projeto de startup (`Api`), `PrivateAssets=all`; migrations em `Infrastructure/Persistence/Migrations` isentas de analyzers via `.editorconfig` | exigência do `dotnet ef`; código gerado não segue o estilo do projeto | 2026-09-18 |
| D-26 | Logs em JSON (`AddJsonConsole`) fora de Development; console legível em Development; `LoggerMessage` com `EventId` fixo | containers/coletores ingerem uma linha JSON por evento; dev lê no terminal | 2026-09-18 |
| D-27 | `Role` como enum e `User.Roles` como `text[]` (primitive collection), sem entidade `Role`/join table | conjunto fixo de 3 papéis; menos tabelas e joins; Npgsql mapeia arrays nativamente | 2026-09-18 |
| D-28 | Value objects (`Money`, `Address`, `CourierInfo`) como **complex types** do EF Core 10 (table splitting), inclusive opcionais; VOs de um campo (`EmailAddress`, `PhoneNumber`) via value converter | semântica de valor (owned types têm identidade e quebram com instâncias compartilhadas); EF 10 passou a suportar complex types opcionais | 2026-09-18 |
| D-29 | `EFCore.NamingConventions` (MIT, mantido pelo autor do Npgsql) para snake_case | identificadores PostgreSQL sem aspas, legíveis em SQL bruto/psql; alternativa (nomear tudo à mão) é verbosa e propensa a erro. Efeito colateral: colunas de `__EFMigrationsHistory` também em snake_case — bancos criados antes precisam ser recriados | 2026-09-18 |
| D-30 | `DbContext` com tracking padrão; leituras usam `AsNoTracking()` explicitamente (não NoTracking global como sugere `efcore-patterns`) | evita `Update()` marcando a entidade inteira como modificada e bugs silenciosos em escritas | 2026-09-18 |
| D-31 | Tokens emitidos com `JsonWebTokenHandler` (Microsoft.IdentityModel.JsonWebTokens 8.x), `MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`, claims mínimas (sem e-mail/nome) | handler atual (span-based, o mesmo que o JwtBearer usa); nomes curtos evitam o mapeamento legado para `ClaimTypes.*`; menos PII no token | 2026-09-18 |
| D-32 | Rate limit de login: janela fixa 5 req/min por `RemoteIpAddress` (sem fila), 429 | simples e suficiente contra força bruta básica; por conta/lockout fica como P2; atrás de proxy exige `ForwardedHeaders` (Fase 16) | 2026-09-18 |
| D-33 | `FallbackPolicy` = autenticado se aplica também a rotas inexistentes: anônimo recebe 401, autenticado recebe 404 | comportamento nativo do middleware de autorização; não revela a superfície de rotas a anônimos; documentado em SECURITY.md §2 | 2026-09-18 |
| D-34 | Falhas de login indistinguíveis (401 + `auth.invalid_credentials`) e verificação contra *decoy hash* quando não há usuário | evitar enumeração por mensagem e por tempo (OWASP A07); custo de 1 PBKDF2 por tentativa, limitado pelo rate limit | 2026-09-18 |
| D-35 | Validação de request com a validação nativa de Minimal APIs do .NET 10 (`AddValidation` + DataAnnotations); confirmada funcional (400 ProblemDetails) | zero dependências; suficiente para forma/obrigatoriedade (D-10 confirmada na prática) | 2026-09-18 |
| D-36 | D-P3 resolvida para a Fase 4: `DeliveryFee` permanece nulo até `MarkDeliveryRequested` (Fase 6); `POST /orders` não cota entrega | não existe provider antes da Fase 6; cotar no pedido seria acoplar o `POST /orders` à latência do provider — reavaliar na Fase 6 com o simulator (custo/benefício de cotação síncrona) | 2026-09-18 |
| D-37 | Idempotência: fingerprint = JSON canônico do request vinculado (não bytes brutos); respostas < 500 são reproduzidas (inclusive 409); chave liberada em falha inesperada | ver ADR-010 "Implementação" | 2026-09-18 |
| D-38 | Paginação keyset pelo `Order.Number` (bigint monotônico da sequence) com cursor base64url opaco | única coluna, indexada, sem tie-breaker; mais simples que (created_at, id) e Guid v7 não é comparável cronologicamente no .NET | 2026-09-18 |
| D-39 | Toda chave gerada pelo cliente (`Guid` v7 no domínio) é configurada `ValueGeneratedNever()` | por convenção o EF trata PK `Guid` como `ValueGeneratedOnAdd`; entidade filha nova descoberta via navegação com chave "já definida" era enviada como UPDATE (0 linhas → `DbUpdateConcurrencyException`) | 2026-09-18 |
| D-40 | Coleções históricas dos agregados (`Order.StatusHistory`, `Payment.Attempts`, `Delivery.Events`) são ordenadas pelo agregado, não pela ordem do banco | `Include` não garante ordem das linhas; teste intermitente revelou | 2026-09-18 |
| D-41 | Reserva de estoque com retry limitado (3×) em `DbUpdateConcurrencyException` antes de responder 409 | conflitos de `xmin` são transitórios; a maioria dos perdedores recebe `insufficient_stock` determinístico em vez de um 409 "tente de novo" | 2026-09-18 |

## Decisões pendentes

| ID | Questão | Opções | Proposta | Quando decidir |
|---|---|---|---|---|
| D-P1 | Idioma final da documentação pública | pt-BR / inglês / ambos | traduzir README + docs principais para inglês na Fase 19 se o alvo incluir vagas internacionais; senão manter pt-BR | Fase 19 |
| D-P2 | Admin: projeto separado (`FulfillmentHub.Admin`) ou hospedar Blazor dentro da Api | separado / junto | **separado** (ciclo de deploy e superfície de ataque distintos) | Fase 17 |
| D-P3 | Momento de cobrar a taxa de entrega | cotar antes do pedido / cobrar depois / taxa fixa | **parcial (D-36)**: Fase 4 mantém `DeliveryFee` nulo até a entrega; decidir cotação síncrona vs. taxa estimada na Fase 6 | Fase 6 |
| D-P4 | Ferramenta de carga | k6 / NBomber | NBomber (.NET-nativo, testes em C#) — mas k6 é mais reconhecido; decidir pelo que gera melhor evidência | Fase 18 |
| D-P5 | Reconciliar via `GET` antes de aplicar webhook `paid`/`refunded` | sim / não | sim (defesa em profundidade contra webhook forjado) | Fase 5 |
| D-P6 | Rede AWS em dev: NAT / VPC endpoints / subnets públicas | A / B / C | C em dev com flag para B | Fase 15 |
| D-P7 | Estado do Terraform | local / S3+DynamoDB | S3+DynamoDB (bootstrap manual) | Fase 15 |
| D-P8 | Simulator persiste estado em memória ou SQLite | memória / SQLite | memória (reinício = limpa) | Fase 6 |
| D-P9 | ~~`git init` local~~ — **resolvida em 2026-09-18**: repositório local criado na Fase 1 (branch `main`, sem remote) | — | — | — |
| D-P10 | ~~Versionamento de API~~ — **resolvida em 2026-09-18**: prefixo fixo `/api/v1` em todos os grupos, sem biblioteca de versioning | — | — | — |
