# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 2 — Fase 1)

## Fase atual
**Fase 1 — Solution .NET + foundation: CONCLUÍDA (Gate 1 fechado, com uma verificação visual pendente — ver "Próximo gate").**
Próxima fase: **Fase 2 — Domínio e banco** (não iniciada).

## Concluído (Fase 1)
- `FulfillmentHub.slnx` com `src/` (Domain, Application, Infrastructure, Api, Worker, ProviderSimulator) e `tests/` (UnitTests, ArchitectureTests, IntegrationTests).
- `global.json` (SDK 10.0.x, runner Microsoft.Testing.Platform), `Directory.Build.props` (nullable, `TreatWarningsAsErrors`, `AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`), `Directory.Packages.props` (CPM, versões verificadas no NuGet em 2026-09-18), `.editorconfig`, `.gitattributes`, `.config/dotnet-tools.json` (dotnet-ef 10.0.12).
- `docker-compose.yml` (profile `deps`: PostgreSQL 17 + Aspire Dashboard como backend OTLP local) + `.env.example`; `.env` local criado (ignorado).
- Domain: `Money` (VO, arredondamento bancário, moeda única) e `DomainException`.
- Application: `IFulfillmentHubDbContext` (seam de persistência, ADR-006).
- Infrastructure: `FulfillmentHubDbContext` (Npgsql), `DatabaseOptions` validadas no start, `AddFulfillmentHubPersistence()`, migration `InitialCreate` (vazia — só histórico), `AddFulfillmentHubTelemetry()` (logs estruturados + OTel traces/metrics/logs; JSON console fora de Development; OTLP só quando `OTEL_EXPORTER_OTLP_ENDPOINT` existe), `TelemetryNames`.
- Api: `ProblemDetails` + `GlobalExceptionHandler` (`DomainException`→422, resto→500), `CorrelationIdMiddleware` (`X-Correlation-Id` validado/gerado, scope de log, tag no span), `/health/live` e `/health/ready` (DbContext check), OpenAPI + Scalar em Development, `TimeProvider` registrado, `public partial class Program`.
- Worker: `AddFulfillmentHubWorker()` (composition root testável), `WorkerOptions`, `WorkerMetrics` (`fh.worker.heartbeats`), `HeartbeatService` (`PeriodicTimer` + `TimeProvider`, parada cooperativa).
- ProviderSimulator: host mínimo com `/health/live`, OTel próprio (sem referência aos projetos FulfillmentHub) e README com disclaimer.
- Testes (28, todos verdes): Unit 11 (Money ×7, HeartbeatService com `FakeTimeProvider` + `MeterListener`), Architecture 5 (direção de dependências, Domain sem pacotes de framework, simulator independente), Integration 12 (health live/ready, ready→503 com banco inacessível, 404 em ProblemDetails, correlation id ecoado/gerado/substituído quando inseguro, migrations aplicadas e modelo sem mudanças pendentes, composição DI do Worker validada, falha rápida sem connection string).
- Verificações: `dotnet build` 0 warnings/0 erros; `dotnet format --verify-no-changes` limpo; `dotnet list package --vulnerable --include-transitive` sem achados; API executada manualmente (health, 404 ProblemDetails com `traceId`, `/openapi/v1.json`, `/scalar/v1`); Worker executado manualmente.
- Git local inicializado (`main`, sem remote), 2 commits; `git ls-files` sem `CLAUDE.md`, `.ai/`, `.claude/`, `.env`.

## Em andamento
- Nada.

## Próximas tarefas (Fase 2 — Domínio e banco; ler antes `fulfillmenthub-dotnet`, `ddd`, `ef-core`, `efcore-patterns`, `modern-csharp`, `testing`)
1. Tipos Common: `Entity<TId>`, `AggregateRoot<TId>` (eventos), `IDomainEvent`, IDs tipados (Guid v7) + conversor EF genérico, `Address`, `EmailAddress`, `PhoneNumber` (BL-001).
2. `Product` (`Reserve/Release`, `IsActive`), configuração EF (`CHECK stock >= 0`, `xmin`), testes (BL-002).
3. `Customer` + endereços owned (BL-003).
4. `Order`/`OrderItem`/`OrderStatusChange` + máquina de estados + eventos; configuração EF (owned Address, sequence de número) (BL-004, BL-010).
5. `Payment`/`PaymentAttempt` + máquina de estados; `UNIQUE` parcial por pedido ativo (BL-005).
6. `DeliveryQuote`, `Delivery`/`DeliveryEvent` + ordem canônica (BL-006).
7. `User`/`Role` (BL-007).
8. `DbSet`s em `IFulfillmentHubDbContext`; reativar `ApplyConfigurationsFromAssembly`; índices (BL-041–043); migration `DomainModel`; testes de constraint (violação → exceção) e de invariantes (DOMAIN.md §10) (BL-008).
9. Seed de desenvolvimento por comando explícito (BL-044, P1).
10. Atualizar DOMAIN.md com o que foi implementado; PROJECT_STATE/BACKLOG/ROADMAP; fechar Gate 2.

## Bloqueadores
- Nenhum. Docker Desktop precisa estar em execução para os testes de integração.

## Decisões tomadas nesta sessão (detalhes em DECISIONS.md D-22…D-26)
- xUnit v3 roda no Microsoft.Testing.Platform (`global.json` → `test.runner`), comandos `dotnet test --solution/--project`.
- Health check do banco via `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` (sem pacote de terceiros).
- Exportador OTLP só é registrado quando há endpoint configurado (testes e ambientes sem coletor não tentam exportar).
- `Microsoft.EntityFrameworkCore.Design` fica no projeto de startup (Api), exigência do `dotnet ef`.
- D-P9 resolvida: `git init` local feito nesta sessão.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P3 taxa de entrega no pedido (Fase 4) · D-P4 k6/NBomber · D-P5 reconciliar antes de aplicar `paid` (Fase 5) · D-P6 rede AWS dev · D-P7 estado Terraform · D-P8 persistência do simulator · D-P10 `/api/v1` fixo.

## Testes atuais
- 28 testes: 11 unit, 5 architecture, 12 integration (Testcontainers PostgreSQL 17). `dotnet test --solution FulfillmentHub.slnx` ≈ 15 s a quente (primeira execução baixa a imagem).

## Infra atual
- Local: `docker compose --profile deps up -d` → Postgres (5432) + Aspire Dashboard (UI 18888, OTLP gRPC 4317). Migration aplicada no banco local. Nenhum remote, GitHub, AWS ou pipeline.
- Segredos locais: user-secrets `Database:ConnectionString` nos projetos Api e Worker; `.env` para o compose (ignorado).

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.

## Próximo gate
**Gate 1 — fechado.** Critérios: build sem warnings ✔; `dotnet test` verde (unit + arch + integration com container) ✔; compose sobe Postgres + Aspire Dashboard ✔; API sobe e `/health/ready` OK ✔; exportador OTLP configurado e porta 4317 do dashboard aceitando conexões ✔ — **confirmação visual do trace na UI (http://localhost:18888) fica a cargo do usuário**, pois não pode ser verificada por linha de comando; `git status` limpo de privados ✔.
**Gate 2 — Core Domain**: 100% das invariantes de DOMAIN.md §10 com teste; migration aplica em banco limpo; constraints `CHECK`/`UNIQUE` existem (teste de integração que tenta violar); ArchitectureTests verdes; `HasPendingModelChanges()` falso.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima (Fase 2), com `docker compose --profile deps up -d` ativo.
