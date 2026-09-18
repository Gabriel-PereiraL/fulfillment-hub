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
| D-42 | D-P5 resolvida = **sim**: webhooks `paid`/`refunded` são confirmados com `GET` no provider antes de aplicar; o status do corpo é só um gatilho | chave HMAC vazada não confirma pagamento; custo de +1 chamada é aceitável para eventos de dinheiro | 2026-09-18 |
| D-43 | D-P8 resolvida = **memória**: o simulator guarda pagamentos/idempotência em `ConcurrentDictionary`; reiniciar limpa | é um dublê de teste, não um sistema; SQLite só adicionaria dependência sem prova de competência | 2026-09-18 |
| D-44 | Pagamento iniciado **in-process** logo após `PlaceOrder` (mesmo request, após o commit do pedido); o resultado do provider nunca falha a criação do pedido — provider fora → pedido fica `Created`, tentativa `TransientFailure`, e a reconciliação recria | o pedido já é durável; sem outbox ainda (Fase 8 move para o worker); manter a promessa "201 = pedido aceito" | 2026-09-18 |
| D-45 | Tentativas `Pending` abandonadas (request morreu entre `StartAttempt` e `CompleteAttempt`) são fechadas como `TransientFailure("abandoned")` na próxima criação | histórico honesto; sem elas o `Payment` fica com uma tentativa eternamente aberta | 2026-09-18 |
| D-46 | Simulator: cenários determinísticos por **valor** (centavos `…99` recusa, `…98` aprova sem webhook), além do `scenario` explícito | os testes de integração e a demo passam pela API real (que não expõe `scenario`); é a convenção de sandbox de PSPs reais | 2026-09-18 |
| D-47 | Captura tardia (provider paga depois de o cliente cancelar) **não** ressuscita o pedido: `Payment` vira `Paid`, pedido segue `Cancelled`, log 5002 + métrica `fh.payments.settled{status=paid_after_cancellation}`; estorno automático em BL-244 | o estado do dinheiro e o do pedido são registrados como estão; um estorno é uma ação de negócio separada, não um efeito colateral escondido de um webhook | 2026-09-18 |
| D-48 | `AddFulfillmentHubApplication()` (casos de uso compartilhados) separado de `AddFulfillmentHubIdentityApplication()` (login/usuários, só a API) | o Worker valida o grafo de DI no build e não tem hasher/token issuer; descoberto por `WorkerCompositionTests` | 2026-09-18 |
| D-49 | `BadHttpRequestException` (corpo ilegível) é mapeada para o **próprio** status 4xx pelo `GlobalExceptionHandler`, com detalhe genérico | em Development `ThrowOnBadRequest=true` transformava JSON inválido em 500; encontrado no smoke manual; fixture de testes agora liga a mesma flag | 2026-09-18 |
| D-50 | `WebhookInbox` faz `SELECT` de existência antes do `INSERT`; a `UNIQUE` continua sendo a garantia | duplicatas são o caso normal (retry do provider) e não devem aparecer como erro de banco nos logs | 2026-09-18 |
| D-51 | D-P3 resolvida: a entrega é **cotada no checkout** (`POST /orders`, antes da transação de estoque) e a taxa cotada entra em `Order.DeliveryFee`/`Total` e é cobrada no pagamento; provider indisponível → taxa estimada `Fulfillment:Origin:EstimatedDeliveryFee` (o pedido nunca depende do provider estar de pé); endereço recusado pelo provider → 400 `order.address_undeliverable`. Ao criar a entrega, a cotação é reutilizada se válida ou refeita; o custo real fica em `Delivery.Fee` e a diferença é absorvida pela loja. Uma única origem de coleta configurada (`Fulfillment:Origin`), sem agregado `Store` | o cliente precisa saber o que paga antes de pagar; recotar depois do pagamento e cobrar a diferença exigiria um segundo pagamento; a taxa estimada mantém a promessa "201 = pedido aceito" | 2026-09-18 |
| D-52 | A entrega é pedida pelo **Worker** (`DeliveryRequestService` varre pedidos `Paid` sem `DeliveryId` a cada 10 s), não dentro do request do webhook de pagamento | um webhook deve responder rápido e não encadear outra chamada externa; a varredura é a outbox "pobre" até a Fase 8 substituí-la | 2026-09-18 |
| D-53 | Pipeline de resiliência extraída para `AddProviderResilienceHandler<TOptions>` + `ProviderResilienceOptions` (base das opções de pagamento e entrega), com `CircuitBreakDurationSeconds` configurável | dois clients com a mesma pipeline; testes de half-open precisam de break curto | 2026-09-18 |
| D-54 | Token do provider de entrega: `DeliveryAccessTokenProvider` (singleton, renovação `TokenRefreshSkewSeconds` antes de expirar, um renovador por vez) + `DeliveryBearerTokenHandler` **dentro** da pipeline de resiliência (401 → renova uma vez e repete); `HttpRequestException` com status segue a mesma regra de retry das respostas (401 do endpoint de token não é repetido) | renovação não deve contar como retry; credencial inválida não é transitória | 2026-09-18 |
| D-55 | Recotação: no máximo **uma** por execução do `RequestDeliveryHandler`; segunda `expired_quote`/`used_quote` → `delivery.quote_expired`, pedido segue `Paid` (operador) e a última tentativa `Requested` guarda a `idempotency_key` para a próxima varredura; rejeições permanentes (`address_undeliverable`, `invalid_params`) cancelam o pedido (`DeliveryFailed`) e devolvem estoque (estorno → BL-244) | T11; nunca cancelar por problema transitório; nunca criar duas entregas | 2026-09-18 |
| D-56 | `409 duplicate_delivery` é reconciliado adotando a entrega existente (`GET metadata.delivery_id`); `Failure.Metadata` carrega dados estruturados de falhas de provider | idempotência de criação após timeout sem duplicar entregas | 2026-09-18 |
| D-57 | Cancelar pedido com entrega ativa cancela **primeiro** no provider; `noncancelable_delivery` → `409 order.delivery_in_progress` e o pedido não muda | o estado local nunca diz "cancelado" enquanto um courier está a caminho | 2026-09-18 |
| D-58 | Simulator de entrega: taxas em reais inteiros e determinísticas por CEP (soma dos dígitos), regras de sandbox por CEP (`00000…` recusado, `…001` cotação de 1 s, `…002` devolvida); `Program` nomeado; caos `RateLimitPerMinute` implementado com janela compartilhada e código por provider | valores sandbox do pagamento dependem dos centavos do total; hash de string é randomizado por processo | 2026-09-18 |
| D-59 | Webhooks de entrega são aplicados **sem** confirmar com `GET` no provider (diferente do pagamento, D-42); a defesa é a assinatura + as regras de ordem do agregado (`Delivery.ApplyProviderEvent`) + a reconciliação periódica | não há dinheiro envolvido; um evento forjado só pode avançar o status e nunca regredi-lo; +1 chamada por evento não paga o custo | 2026-09-18 |
| D-60 | Recepção de webhooks unificada em `WebhookReceiver` (limite, assinatura, parse, inbox, processamento, sempre 200); cada endpoint declara só `WebhookSource` + identificação/processamento do payload; métricas de webhook em `WebhooksMetrics` (fora de `PaymentsMetrics`) | dois providers com o mesmo ciclo; evitar divergência sutil entre os endpoints | 2026-09-18 |
| D-61 | O pedido acompanha a entrega **somente** quando a disposição do evento é `Applied`: `pickup_complete`/`dropoff` → `InDelivery`, `delivered` → `Delivered` (passando por `InDelivery` se o pickup se perdeu), `canceled`/`returned` → `Cancelled(DeliveryFailed)` + estoque (estorno → BL-244); pedido já cancelado pelo cliente ignora o `canceled` do provider | eventos `Stale`/`OutOfOrder`/`Duplicate` são registrados para auditoria mas não movem nada | 2026-09-18 |
| D-62 | Reconciliação de entregas aplica o status atual do provider como evento sintético (`reconciled:<id>:<status>:<updated>`) pelas mesmas regras dos webhooks; jobs do Worker compartilham a base `PeriodicJob` (escopo por execução, erro logado, loop continua) | reconciliação nunca pode regredir uma entrega; três jobs com o mesmo esqueleto | 2026-09-18 |
| D-63 | Simulator: CEP `…003` = entrega sem webhooks; `Simulator:Delivery:WebhookUrl` ligado em Development e na fixture (`CourierAssignMs`/`StepMs` = 500 ms nos testes) | cenário de webhook perdido determinístico; testes ponta a ponta com webhooks reais em poucos segundos | 2026-09-18 |

## Decisões pendentes

| ID | Questão | Opções | Proposta | Quando decidir |
|---|---|---|---|---|
| D-P1 | Idioma final da documentação pública | pt-BR / inglês / ambos | traduzir README + docs principais para inglês na Fase 19 se o alvo incluir vagas internacionais; senão manter pt-BR | Fase 19 |
| D-P2 | Admin: projeto separado (`FulfillmentHub.Admin`) ou hospedar Blazor dentro da Api | separado / junto | **separado** (ciclo de deploy e superfície de ataque distintos) | Fase 17 |
| D-P3 | ~~Momento de cobrar a taxa de entrega~~ — **resolvida em 2026-09-18 (D-51): cotação síncrona no checkout com taxa estimada como fallback** | — | — | — |
| D-P4 | Ferramenta de carga | k6 / NBomber | NBomber (.NET-nativo, testes em C#) — mas k6 é mais reconhecido; decidir pelo que gera melhor evidência | Fase 18 |
| D-P5 | ~~Reconciliar via `GET` antes de aplicar webhook `paid`/`refunded`~~ — **resolvida em 2026-09-18 (D-42): sim** | — | — | — |
| D-P6 | Rede AWS em dev: NAT / VPC endpoints / subnets públicas | A / B / C | C em dev com flag para B | Fase 15 |
| D-P7 | Estado do Terraform | local / S3+DynamoDB | S3+DynamoDB (bootstrap manual) | Fase 15 |
| D-P8 | ~~Simulator persiste estado em memória ou SQLite~~ — **resolvida em 2026-09-18 (D-43): memória** | — | — | — |
| D-P9 | ~~`git init` local~~ — **resolvida em 2026-09-18**: repositório local criado na Fase 1 (branch `main`, sem remote) | — | — | — |
| D-P10 | ~~Versionamento de API~~ — **resolvida em 2026-09-18**: prefixo fixo `/api/v1` em todos os grupos, sem biblioteca de versioning | — | — | — |
