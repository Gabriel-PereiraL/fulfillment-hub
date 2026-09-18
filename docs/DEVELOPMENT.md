# DEVELOPMENT — guia do desenvolvedor

## 1. Pré-requisitos (verificados em 2026-09-18 nesta máquina)

| Ferramenta | Versão encontrada | Necessário |
|---|---|---|
| .NET SDK | 10.0.400 (runtime 10.0.11) — LTS | 10.0.x |
| Git | 2.54.0.windows.1 | qualquer recente |
| Docker Engine / Compose | 29.4.3 / v5.1.3 | Docker Desktop com WSL2 rodando (Testcontainers) |
| Terraform | **não instalado** | só na Fase 15 (instalar via winget/choco) |
| AWS CLI | **não instalado** | só na Fase 15 (instalar; usar SSO/credenciais temporárias) |
| SO | Windows 11 Pro | shell: PowerShell 5.1 ou Git Bash |

## 2. Primeiro setup (a partir da Fase 1)

```bash
git clone <local>   # nesta fase o repositório é apenas local
cd FulfillmentHub
dotnet --version                       # 10.0.x
docker compose --profile deps up -d    # postgres, aspire-dashboard (+ localstack, simulator conforme fase)
dotnet user-secrets init --project src/FulfillmentHub.Api
dotnet user-secrets set "ConnectionStrings:FulfillmentHub" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<dev>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Jwt:SigningKey" "<32+ bytes aleatórios>" --project src/FulfillmentHub.Api
dotnet tool restore                    # dotnet-ef (manifest em .config/dotnet-tools.json)
dotnet ef database update --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet run --project src/FulfillmentHub.Api            # https://localhost:5001 — /scalar (OpenAPI UI), /health/ready
dotnet run --project src/FulfillmentHub.Worker
dotnet run --project src/FulfillmentHub.ProviderSimulator   # http://localhost:5100
```

Aspire Dashboard: http://localhost:18888 (traces/logs/métricas).

## 3. Comandos do dia a dia

```bash
dotnet build FulfillmentHub.sln                    # com TreatWarningsAsErrors
dotnet test                                        # unit + architecture + integration (Docker)
dotnet test --filter "Category!=E2E"
dotnet format                                      # aplica .editorconfig
dotnet ef migrations add <Nome> --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet ef migrations script --idempotent -o artifacts/migrate.sql --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet list package --vulnerable --include-transitive
dotnet run --project src/FulfillmentHub.Api -- seed   # seed de dev (comando explícito; Fase 2)
```

## 4. Configuração e segredos

- `appsettings.json` (defaults sem segredos) → `appsettings.Development.json` (portas, níveis de log) → **user-secrets** (dev) → variáveis de ambiente (containers/AWS).
- Nunca commitar segredo. `.env.example` documenta as variáveis do compose. Lista de chaves esperadas (Fase 1): `ConnectionStrings:FulfillmentHub`, `Jwt:SigningKey`, `Jwt:Issuer`, `Jwt:Audience`, `Providers:Delivery:BaseUrl`, `Providers:Delivery:ClientId/ClientSecret` (fake), `Providers:Delivery:WebhookSigningKey` (fake), `Providers:Payment:*`, `Messaging:Sqs:*` (Fase 9), `OTEL_EXPORTER_OTLP_ENDPOINT`.
- Options tipadas com `ValidateOnStart`: a aplicação **não sobe** com configuração inválida — é intencional.

## 5. Convenções de código
Ver `.editorconfig` (Fase 1) e a skill principal (privada). Resumo público: C# 14, nullable habilitado, warnings como erro, file-scoped namespaces, `sealed` por padrão, records para DTOs, entidades com comportamento, `CancellationToken` em todo método assíncrono, `TimeProvider` para tempo, sem `.Result/.Wait()`.

## 6. Fluxo de trabalho por tarefa
1. Ler `PROJECT_STATE.md` → item do `BACKLOG.md` (P0 da fase atual).
2. Implementar em fatias pequenas; build e testes verdes a cada fatia.
3. Testes conforme `TEST_STRATEGY.md` (matriz).
4. Atualizar docs afetados + `PROJECT_STATE.md`.
5. Commit local (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`), após `git status` limpo de arquivos privados.

## 7. Estrutura de pastas (ver ARCHITECTURE.md §2) e onde colocar as coisas
- Nova regra de negócio → `Domain/<Módulo>/`, teste em `UnitTests/<Módulo>/`.
- Novo endpoint → `Api/<Módulo>/<Módulo>Endpoints.cs` + caso de uso em `Application/<Módulo>/` + teste em `IntegrationTests/<Módulo>/`.
- Nova tabela → entidade + `Infrastructure/Persistence/Configurations/<Módulo>/` + migration.
- Nova chamada externa → porta em `Application/<Módulo>/`, adapter em `Infrastructure/Providers/`, política em `Infrastructure/Providers/Resilience`.

## 8. Troubleshooting (será alimentado a cada fase)
- Testcontainers no Windows: Docker Desktop precisa estar em execução com backend WSL2; primeira execução baixa imagens (lento).
- `dotnet ef` não encontrado: `dotnet tool restore`.
- Aplicação não sobe por Options inválidas: ler a mensagem de validação; configurar user-secrets.
