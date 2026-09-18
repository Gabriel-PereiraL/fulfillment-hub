# ROADMAP — FulfillmentHub

Roadmap incremental por fases com **gate** ao final de cada uma. Nenhuma fase avança sem os critérios de aceite.
Status: `todo` · `in-progress` · `done` · `blocked`. Ordem ajustada em relação à proposta original: **Auth veio antes de
Orders** (pedido precisa de um principal; retrofitar autenticação depois gera retrabalho), **Docker Compose de dependências
entrou na Fase 1** (banco local é necessário desde o início) e **observabilidade básica** (logs estruturados, correlation id,
OTel skeleton) também entrou na Fase 1 (invariante: não é pós-projeto). Fases de CI e AWS exigem **autorização explícita**
do usuário para ações externas (GitHub, conta AWS, custos).

| # | Fase | Status | Gate |
|---|---|---|---|
| 0 | Documentação e arquitetura | **done (2026-09-18)** | Gate 0 |
| 1 | Solution .NET + foundation | **done (2026-09-18)** | Gate 1 |
| 2 | Domínio e banco | **done (2026-09-18)** | Gate 2 |
| 3 | Identity + Auth (JWT, roles) | todo | Gate 3 |
| 4 | Orders (API + idempotência + concorrência de estoque) | todo | Gate 4 |
| 5 | Payments (simulator + integração + webhook + reconciliação) | todo | Gate 5 |
| 6 | Delivery provider simulator (Uber-like) + integração de saída resiliente | todo | Gate 6 |
| 7 | Webhooks de entrega + idempotência + eventos fora de ordem | todo | Gate 7 |
| 8 | Transactional outbox + Worker | todo | Gate 8 |
| 9 | SQS (LocalStack) — producer/consumer, DLQ, idempotent consumer | todo | Gate 9 |
| 10 | Security hardening (threat model, OWASP, rate limit, headers, secrets) | todo | Gate 10 |
| 11 | Observability hardening (métricas, traces, dashboards locais, runbook) | todo | Gate 11 |
| 12 | Testing hardening (E2E, contract tests, chaos via simulator) | todo | Gate 12 |
| 13 | Docker images + compose completo | todo | Gate 13 |
| 14 | CI (GitHub Actions) — **requer autorização para GitHub** | todo | Gate 14 |
| 15 | AWS IaC (Terraform) — **requer conta AWS/autorização de custo** | todo | Gate 15 |
| 16 | Cloud deployment (ECS Fargate, RDS, SQS, Secrets, CloudWatch alerts) | todo | Gate 16 |
| 17 | Admin/Ops UI (Blazor) | todo | Gate 17 |
| 18 | Performance & resilience tests | todo | Gate 18 |
| 19 | Documentation hardening | todo | Gate 19 |
| 20 | Portfolio release (revisão anti-vazamento, README final) | todo | Gate 20 |

---

## Fase 0 — Documentação e arquitetura — `done`
**Objetivo**: base documental suficiente para "continue o projeto" funcionar sem contexto extra.
**Tasks**: inspecionar ambiente; escolher .NET; pesquisar Uber Direct e skills; nome; arquitetura; domínio; docs; roadmap; backlog; ADRs; PROJECT_STATE; .gitignore; CLAUDE.md; skills privadas; índice de skills.
**Critérios de aceite (Gate 0)**: todos os arquivos de `docs/` + `docs/adr/` existem e são coerentes; CLAUDE.md aponta para PROJECT_STATE e SKILLS_INDEX; `.gitignore` cobre privados/secrets; teste manual de "nova sessão" descrito em `.ai/CHECKLIST.md`.
**Dependências**: nenhuma. **Riscos**: over-documentar sem código (mitigado: Fase 1 começa na próxima sessão).

## Fase 1 — Solution .NET + foundation — `done`
**Objetivo**: esqueleto executável, compilando com analyzers, banco local, migrations, health check, primeiros testes, telemetria mínima.
**Tasks**:
1. `dotnet new sln`; projetos `Domain`, `Application`, `Infrastructure`, `Api`, `Worker`, `ProviderSimulator`; testes `UnitTests`, `IntegrationTests`, `ArchitectureTests`.
2. `Directory.Build.props` (Nullable, TreatWarningsAsErrors, AnalysisLevel latest-recommended, LangVersion), `Directory.Packages.props` (CPM), `.editorconfig`, `global.json` (SDK 10.0.x).
3. `docker-compose.yml`: PostgreSQL 17, Aspire Dashboard (OTLP), LocalStack (só a partir da Fase 9 — deixar comentado).
4. `FulfillmentHubDbContext` vazio + migration inicial + `dotnet ef` funcionando; `IFulfillmentHubDbContext`.
5. Options tipadas (`ConnectionStrings`, `Telemetry`), user-secrets configurados, `appsettings.Development.json` sem secrets.
6. Health checks: `/health/live`, `/health/ready` (Npgsql).
7. ProblemDetails + `IExceptionHandler`; OpenAPI + Scalar em Development.
8. Logging estruturado (JSON console), correlation id middleware (`X-Correlation-Id`), OpenTelemetry (ASP.NET Core, HttpClient, Npgsql) → OTLP.
9. ArchitectureTests: direção de dependências; UnitTests: 1 teste de `Money`; IntegrationTests: `WebApplicationFactory` + Testcontainers PostgreSQL rodando `/health/ready`.
10. `git init` local (sem remote); primeiro commit após `git status` limpo de privados.
**Critérios de aceite (Gate 1)**: `dotnet build` sem warnings; `dotnet test` verde (unit + arch + integration com container); `docker compose up` sobe Postgres e Aspire Dashboard; API sobe, `/health/ready` OK, trace visível no dashboard; `git status` não mostra arquivos privados.
**Dependências**: Docker Desktop rodando. **Riscos**: versões de pacotes .NET 10 (validar no NuGet na sessão); Testcontainers no Windows (Docker Desktop com WSL2).
**Resultado (2026-09-18)**: todos os critérios atendidos; 28 testes verdes; confirmação visual do trace no Aspire Dashboard a cargo do usuário. Ajustes em relação ao plano: testes rodam no Microsoft.Testing.Platform (xunit.v3 + .NET 10 SDK); health check do banco via pacote EF Core da Microsoft; migration inicial vazia (modelo chega na Fase 2).

## Fase 2 — Domínio e banco — `done`
**Objetivo**: modelo de domínio dos módulos Catalog, Customers, Orders, Payments, Deliveries, Identity com invariantes testadas e schema consistente.
**Tasks**: tipos Common (Entity, AggregateRoot, Money, Address, IDs); entidades e máquinas de estado; configurações EF (owned/complex types, conversores, `xmin`, constraints, índices, sequence de número de pedido); migration; seed de desenvolvimento (produtos, usuário admin) via comando explícito; testes de unidade das invariantes (DOMAIN.md §10).
**Gate 2**: 100% das invariantes listadas têm teste; migration aplica em banco limpo; constraints `CHECK`/`UNIQUE` existem (teste de integração que tenta violar); ArchitectureTests verdes.
**Riscos**: over-modelagem — manter só o que os fluxos usam.
**Resultado (2026-09-18)**: 6 módulos implementados (Catalog, Customers, Orders, Payments, Deliveries, Identity), migration `DomainModel`, 83 testes de unidade + 8 de integração (round-trips, CHECK, unique parcial, conflito `xmin`); `HasPendingModelChanges()` falso. Divergências registradas em DOMAIN.md §12 e D-27…D-30. Seed de desenvolvimento (BL-044) movido para a Fase 3 (precisa do hash de senha).

## Fase 3 — Identity + Auth
**Objetivo**: usuários, papéis, login com JWT, políticas; API nega por padrão.
**Tasks**: `User`/`Role`; `PasswordHasher`; `POST /auth/login` (rate-limited); emissão JWT (HS256, chave via secrets, expiração curta); `FallbackPolicy`; policies `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly`; `GET /me`; seed admin via comando; testes (login ok/falha, 401/403, token expirado).
**Gate 3**: endpoints protegidos por padrão; testes de autorização por papel; segredo JWT fora do código; SECURITY.md atualizado com o desenho.
**Riscos**: tentação de usar ASP.NET Identity completo — não; só `PasswordHasher<T>`.

## Fase 4 — Orders
**Objetivo**: criar/consultar/cancelar pedidos com idempotência real e concorrência de estoque demonstrada.
**Tasks**: `POST /orders` (Idempotency-Key filtro + `IdempotencyRecord`), `GET /orders/{id}`, `GET /orders` (paginação keyset), `POST /orders/{id}/cancel`; reserva de estoque com `xmin`; histórico de status; decisão D-P3 (taxa de entrega no pedido); testes: idempotência (mesma chave → mesma resposta; hash diferente → 422; concorrente → 409), race de estoque (N tarefas paralelas → nunca negativo), transições.
**Gate 4**: cenário de race condition tem teste que falha sem o token de concorrência e passa com ele; idempotência coberta; OpenAPI com todos os endpoints; BACKLOG P0 de Orders fechado.
**Riscos**: idempotência com respostas grandes — armazenar body (jsonb) com TTL.

## Fase 5 — Payments
**Objetivo**: provider de pagamento simulado + integração + webhook idempotente + reconciliação + falhas temporárias/permanentes.
**Tasks**: simulator `/payments/v1` (contrato INTEGRATIONS §3, cenários); `IPaymentGatewayClient` typed client + resiliência; `CreatePaymentForOrder` (in-process, disparado logo após `PlaceOrder` nesta fase; via outbox na Fase 8); `POST /webhooks/payments` (assinatura, dedup, processamento); `ReconcilePayments` job (Worker mínimo); transições de `Payment`/`Order`; testes: webhook duplicado, atraso, falha temporária (retry) vs permanente (cancela pedido e libera estoque).
**Gate 5**: fluxo pedido→pago passa em integração com o simulator in-process; webhook duplicado não gera efeito duplo; reconciliação corrige pendente.
**Riscos**: complexidade do simulator — manter em memória e pequeno.

## Fase 6 — Delivery provider simulator + integração de saída
**Objetivo**: simulator "Uber-like" (subconjunto documentado) e cliente resiliente de cotação/criação/consulta/cancelamento.
**Tasks**: rotas `/delivery/v1` (token, quotes, deliveries, get, cancel) com erros/cenários de INTEGRATIONS §2; ciclo de vida temporizado; `IDeliveryProviderClient` + pipeline de resiliência + matriz de retry; `DeliveryQuote`/`Delivery` criação; recotação em `expired_quote`; `409 duplicate_delivery` reconciliado; testes com `HttpMessageHandler` fake (429 + Retry-After, 503, timeout, 400 sem retry) e integração com simulator in-process.
**Gate 6**: matriz de retry coberta por testes; circuit breaker abre/fecha em teste; disclaimer presente no README do simulator.

## Fase 7 — Webhooks de entrega + idempotência + fora de ordem
**Objetivo**: ingestão de `event.delivery_status` com assinatura, dedup e regra de ordem.
**Tasks**: `POST /webhooks/deliveries`; `WebhookEvent`; `ApplyDeliveryWebhookHandler` com ordem canônica; `Order` acompanha (`InDelivery`, `Delivered`, `Cancelled`); simulator enviando webhooks com duplicidade/atraso/fora de ordem; testes: `delivered` antes de `pickup` não regride; duplicado → no-op; assinatura inválida → 401.
**Gate 7**: cenário "webhook fora de ordem" e "duplicado" com testes de integração verdes; E2E manual: pedido chega a `Delivered` com simulator em modo caótico.

## Fase 8 — Transactional outbox + Worker
**Objetivo**: eliminar dual-write: eventos de domínio persistidos no commit e processados pelo Worker com retry/DLQ lógica.
**Tasks**: `OutboxMessage`, interceptor, `OutboxPublisher` (`SKIP LOCKED`), despacho in-process para handlers (`OrderPlaced`→pagamento, `OrderPaid`→entrega, `OrderCancelled`→estoque/estorno), backoff+jitter, `Failed` após N, endpoint/admin de reprocessamento, métricas de lag; migrar disparos in-process das fases 5–6 para outbox; testes: falha injetada entre commit e publicação não perde evento; handler falho é reexecutado; após N vai a `Failed`.
**Gate 8**: nenhum efeito externo fora do worker; teste de "crash após commit" passa; ADR-004 atualizado com o que foi implementado.

## Fase 9 — SQS
**Objetivo**: outbox publica no SQS; consumidores no Worker; DLQ; idempotent consumer; LocalStack local; Testcontainers LocalStack.
**Tasks**: `IMessagePublisher` (AWS SDK), filas + DLQ criadas por script/compose init; consumidores com long polling, visibilidade, concorrência limitada; `processed_messages`; webhooks passam a ir para `fh-webhooks-inbound`; métricas (profundidade aproximada, idade da mensagem, DLQ count); testes de integração com LocalStack; documentação do trade-off (INTEGRATIONS §6).
**Gate 9**: mensagem envenenada vai à DLQ após `maxReceiveCount`; consumidor recebe duplicata e não duplica efeito; compose completo funciona.

## Fase 10 — Security hardening
**Objetivo**: SECURITY.md "implementado", não "planejado".
**Tasks**: threat model revisado; rate limiting (login, webhooks, criação); security headers; CORS explícito; validação/limites de tamanho; mass assignment revisado; logs sem PII (redação); `dotnet list package --vulnerable` no build local; segredos auditados; autorização por recurso (cliente só vê seus pedidos) com testes; checklist OWASP Top 10 preenchido com evidências.
**Gate 10**: checklist com link para código/teste em cada item; testes de broken access control.

## Fase 11 — Observability hardening
**Objetivo**: métricas/traces/logs úteis para operar; runbook de incidente.
**Tasks**: métricas próprias (OBSERVABILITY.md); spans em worker/outbox/consumers com propagação de contexto (trace parent salvo no outbox e na mensagem SQS); dashboards locais (Aspire Dashboard + queries salvas); alertas planejados (thresholds); runbook "cliente relata lentidão"; teste que verifica que um pedido gera um único `trace_id` de ponta a ponta.
**Gate 11**: runbook executado de verdade em local com simulator lento e evidências (prints/registro em docs).

## Fase 12 — Testing hardening
**Objetivo**: E2E de fluxos principais, contract tests do simulator, chaos.
**Tasks**: E2E com compose (pedido→entrega, pedido→pagamento falho, pedido→entrega cancelada); contract tests (schemas do simulator vs. DTOs do cliente); execução com `SIM_FAILURE_RATE=0.3` e asserção de convergência; cobertura de mutação opcional (Stryker) em Domain; limpeza de testes fracos.
**Gate 12**: E2E verdes; TEST_STRATEGY.md com matriz "cenário → teste".

## Fase 13 — Docker images + compose completo
**Objetivo**: Dockerfiles multi-stage (Api, Worker, Simulator, Admin), usuário não-root, healthcheck; compose sobe tudo.
**Gate 13**: `docker compose up --build` → fluxo E2E passa contra containers; imagens < 250 MB; scan local (Trivy/`docker scout`) sem críticos.

## Fase 14 — CI (GitHub Actions) — requer autorização
**Objetivo**: pipeline restore/build/analyzers/unit/integration (Testcontainers)/security scan (CodeQL, dependency review, gitleaks, Trivy)/build image/artifact.
**Pré-condição**: usuário autoriza criação do repositório remoto; revisão anti-vazamento (DEPLOYMENT.md §checklist) executada.
**Gate 14**: pipeline verde em PR; badges no README; sem secrets em workflows.

## Fase 15 — AWS IaC (Terraform) — requer conta/autorização de custo
**Objetivo**: módulos Terraform: VPC, subnets, SG, ECR, ECS/Fargate, ALB, RDS PostgreSQL, SQS+DLQ, IAM least privilege, Secrets Manager, CloudWatch; `plan` revisado; budget alarm.
**Gate 15**: `terraform plan` limpo e revisado; custo estimado documentado; `terraform destroy` testado.

## Fase 16 — Cloud deployment
**Objetivo**: ambiente `dev` na AWS operando (deploy via CI ou manual documentado), migrations aplicadas por task one-off, alertas CloudWatch (5xx, p95, DLQ > 0, outbox lag).
**Gate 16**: fluxo E2E executado na nuvem com simulator hospedado no ECS; alerta disparado de propósito e registrado; ambiente destruído ao final (custo).

## Fase 17 — Admin/Ops UI (Blazor)
**Objetivo**: telas de pedidos, pagamentos, entregas, webhooks, outbox, DLQ, integrações; ações de reprocessar/cancelar/reconciliar; autorização Operator/Admin.
**Gate 17**: operador reprocessa uma mensagem falha sem tocar no banco; testes de componentes principais (bUnit) e de autorização.

## Fase 18 — Performance & resilience tests
**Objetivo**: carga (k6 ou NBomber — decidir) em `POST /orders` e webhooks; simulator caótico; medir p95, taxa de erro, lag de outbox; ajustes.
**Gate 18**: relatório com números reais (sem inventar), gargalos identificados e tratados ou registrados.

## Fase 19 — Documentation hardening
**Objetivo**: README público final, diagramas, ADRs revisadas, decisão de idioma (tradução para inglês?), guia de leitura para recrutador.
**Gate 19**: revisão de "tudo que o projeto afirma é verdadeiro".

## Fase 20 — Portfolio release
**Objetivo**: revisão anti-vazamento final, tag `v1.0.0`, publicação (com autorização).
**Gate 20**: checklist de publicação (DEPLOYMENT.md) 100%; nenhum arquivo privado no histórico do Git.
