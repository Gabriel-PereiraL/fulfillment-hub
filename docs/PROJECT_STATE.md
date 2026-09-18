# Estado atual — FulfillmentHub

> Memória operacional do projeto. **Sempre atualizar ao terminar uma sessão.**
> Comando para próxima sessão: "Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar."

## Última atualização
2026-09-18 (sessão 1 — Fase 0)

## Fase atual
**Fase 0 — Documentação e arquitetura: CONCLUÍDA (Gate 0 fechado).**
Próxima fase: **Fase 1 — Solution .NET + foundation** (não iniciada).

## Concluído
- Ambiente inspecionado: .NET SDK 10.0.400 (LTS), Git 2.54, Docker 29.4.3 / Compose v5.1.3; Terraform e AWS CLI ausentes (só na Fase 15).
- `Serraf-Pessoas` localizado em `C:\Users\US-JS000184\Desktop\DP\RH\Serraf-Pessoas`; estrutura (`CLAUDE.md` raiz + `.claude/skills/<nome>/SKILL.md`) usada como referência.
- Skills C#/.NET pesquisadas no GitHub; 3 repositórios MIT clonados e avaliados; 40 skills copiadas para `.claude/skills/` (privado) + skill própria `fulfillmenthub-dotnet` com revisão crítica. Índice em `.ai/SKILLS_INDEX.md`.
- Documentação oficial do Uber Direct estudada (portal + spec OpenAPI do SDK oficial); contrato real vs. simulador documentado em `docs/INTEGRATIONS.md`; spec copiada para `.ai/reference/` (privado).
- Nome definido: FulfillmentHub. Arquitetura (monólito modular, 3 processos), domínio, roadmap (21 fases), backlog (BL-001…BL-243), 13 ADRs, estratégia de testes, segurança, observabilidade, AWS alvo, deployment, development.
- `CLAUDE.md` (privado), `.gitignore`, `.ai/` (SESSION, NOTES, CHECKLIST, SKILLS_INDEX), `README.md` público com disclaimer.

## Em andamento
- Nada. (Sessão encerrada com Gate 0 fechado.)

## Próximas tarefas (Fase 1 — ordem sugerida)
1. Verificar no NuGet as versões estáveis para .NET 10 (EF Core/Npgsql, OpenTelemetry, Http.Resilience, xUnit v3, Shouldly, Testcontainers, NetArchTest, Scalar) e fixar em `Directory.Packages.props`.
2. Criar `FulfillmentHub.sln`, projetos `src/*` (Domain, Application, Infrastructure, Api, Worker, ProviderSimulator) e `tests/*` (UnitTests, IntegrationTests, ArchitectureTests); `global.json`, `Directory.Build.props` (Nullable, TreatWarningsAsErrors, AnalysisLevel latest-recommended), `.editorconfig`.
3. `docker-compose.yml` com PostgreSQL 17 e Aspire Dashboard (profile `deps`); `.env.example`.
4. `FulfillmentHubDbContext` + `IFulfillmentHubDbContext` + migration inicial (vazia/`__EFMigrationsHistory`) + `dotnet ef` via tool manifest.
5. Api: Options tipadas validadas, user-secrets, ProblemDetails + `IExceptionHandler`, OpenAPI + Scalar (dev), health checks, correlation id middleware, logging JSON + OpenTelemetry → OTLP.
6. Worker: host vazio com `BackgroundService` de heartbeat (métrica) e mesma telemetria.
7. ProviderSimulator: host vazio com `/health` e disclaimer no `README.md` do projeto.
8. Testes: ArchitectureTests (dependências), UnitTests (`Money`), IntegrationTests (`WebApplicationFactory` + Testcontainers PostgreSQL → `/health/ready` 200).
9. `git init` local (sem remote); `git status` sem privados; primeiro commit `chore: bootstrap solution`.
10. Atualizar PROJECT_STATE/BACKLOG/ROADMAP; fechar Gate 1 se critérios OK.

## Bloqueadores
- Nenhum. (Docker Desktop precisa estar em execução para Testcontainers na Fase 1.)

## Decisões tomadas (resumo; detalhes em DECISIONS.md e adr/)
- Monólito modular, 3 processos; PostgreSQL + EF Core; simuladores próprios; outbox; SQS/LocalStack; sem repository/MediatR; Minimal APIs + ProblemDetails + validação nativa; JWT próprio; OTel com logging nativo + Aspire Dashboard; idempotência em 4 fronteiras; ECS Fargate + Terraform; xUnit v3 + Shouldly + Testcontainers; .NET 10.
- Docs em pt-BR, código em inglês; Auth antes de Orders; Compose e observabilidade básica na Fase 1.

## Decisões pendentes
D-P1 idioma final da doc · D-P2 Admin separado · D-P3 taxa de entrega no pedido (Fase 4) · D-P4 k6/NBomber · D-P5 reconciliar antes de aplicar `paid` (Fase 5) · D-P6 rede AWS dev · D-P7 estado Terraform · D-P8 persistência do simulator · D-P9 `git init` na Fase 1 · D-P10 `/api/v1` fixo.

## Testes atuais
- Nenhum (sem código). Estratégia definida em `TEST_STRATEGY.md` (matriz T1–T20).

## Infra atual
- Local apenas. Nenhum container, repositório remoto, conta AWS ou pipeline criados. Nenhuma ação externa executada além de leitura (pesquisa web e clones read-only em `%TEMP%`).

## Skills privadas disponíveis
- `.claude/skills/` — 41 skills (ver `.ai/SKILLS_INDEX.md`). Obrigatória para C#: `fulfillmenthub-dotnet`.
- Pendências: sem skill de Terraform/AWS (usar docs oficiais); `messaging` genérica (complementar com AWS SDK docs na Fase 9).

## Próximo gate
**Gate 1 — Foundation**: solution compila sem warnings; `dotnet test` verde (unit + arch + integration com Testcontainers); compose sobe Postgres + Aspire Dashboard; `/health/ready` OK com trace visível; `git status` limpo de privados.

## Comando para próxima sessão
"Leia PROJECT_STATE.md, ROADMAP.md, BACKLOG.md e DECISIONS.md antes de continuar." — depois, começar pela tarefa 1 da lista acima
(ler antes a skill `fulfillmenthub-dotnet` e as skills `project-structure`, `modern-csharp`, `ef-core`, `testing`, `testcontainers`, `configuration`, `opentelemetry`).
