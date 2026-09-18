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

## 2. Primeiro setup

```bash
cd FulfillmentHub                      # nesta fase o repositório é apenas local
dotnet --version                       # 10.0.x
cp .env.example .env                   # ajuste POSTGRES_PASSWORD (valor local; .env é ignorado pelo Git)
docker compose --profile deps up -d    # postgres (5432) + aspire-dashboard (UI 18888, OTLP gRPC 4317)
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<o mesmo do .env>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<o mesmo do .env>" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Jwt:SigningKey" "<64 chars aleatórios, ex.: openssl rand -base64 48>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Seed:AdminPassword" "<senha dev, 12+ chars>" --project src/FulfillmentHub.Api      # idem Seed:OperatorPassword e Seed:CustomerPassword
# Provider de pagamento (Fase 5): os valores precisam bater com src/FulfillmentHub.ProviderSimulator/appsettings.Development.json (dev-only, não são segredos reais)
dotnet user-secrets set "Providers:Payment:ApiKey" "dev-only-payment-api-key" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Payment:WebhookSigningKey" "dev-only-payment-webhook-signing-key" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Payment:ApiKey" "dev-only-payment-api-key" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Providers:Payment:WebhookSigningKey" "dev-only-payment-webhook-signing-key" --project src/FulfillmentHub.Worker
# Provider de entrega (Fase 6): idem, valores dev-only de src/FulfillmentHub.ProviderSimulator/appsettings.Development.json (Simulator:Delivery)
dotnet user-secrets set "Providers:Delivery:ClientId" "dev-only-delivery-client-id" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Delivery:ClientSecret" "dev-only-delivery-client-secret" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Delivery:WebhookSigningKey" "dev-only-delivery-webhook-signing-key" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Delivery:ClientId" "dev-only-delivery-client-id" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Providers:Delivery:ClientSecret" "dev-only-delivery-client-secret" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Providers:Delivery:WebhookSigningKey" "dev-only-delivery-webhook-signing-key" --project src/FulfillmentHub.Worker
dotnet tool restore                    # dotnet-ef (manifest em .config/dotnet-tools.json)
dotnet ef database update --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet run --project src/FulfillmentHub.Api -- seed       # dados fictícios de desenvolvimento (só Development; usuários admin@/operator@/customer.local)
dotnet run --project src/FulfillmentHub.Api               # http://localhost:5000 — /scalar/v1 (OpenAPI UI), /health/live, /health/ready
dotnet run --project src/FulfillmentHub.Worker
dotnet run --project src/FulfillmentHub.ProviderSimulator # http://localhost:5100/health/live — envia webhooks para http://localhost:5000/api/v1/webhooks/payments
```

Aspire Dashboard: http://localhost:18888 (traces/logs/métricas). Os hosts exportam OTLP quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definido (já está em `appsettings.Development.json`).

## 3. Comandos do dia a dia

```bash
dotnet build FulfillmentHub.slnx                        # TreatWarningsAsErrors + analyzers
dotnet test --solution FulfillmentHub.slnx              # unit + architecture + integration (Docker necessário)
dotnet test --project tests/FulfillmentHub.UnitTests    # um projeto
dotnet format FulfillmentHub.slnx --verify-no-changes   # estilo (.editorconfig)
dotnet ef migrations add <Nome> --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent -o artifacts/migrate.sql --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet list FulfillmentHub.slnx package --vulnerable --include-transitive
```

Os testes usam o Microsoft.Testing.Platform (`global.json` → `test.runner`): use `--solution`/`--project`, não o caminho posicional.

## 4. Configuração e segredos

- `appsettings.json` (defaults sem segredos) → `appsettings.Development.json` (portas, níveis de log) → **user-secrets** (dev) → variáveis de ambiente (containers/AWS).
- Nunca commitar segredo. `.env.example` documenta as variáveis do compose. Lista de chaves esperadas: `Database:ConnectionString` (Fase 1), `Jwt:SigningKey` (secret, ≥ 32 chars; `Jwt:Issuer`/`Audience` têm defaults em appsettings), `Seed:AdminPassword`/`OperatorPassword`/`CustomerPassword` (só para o comando `seed`), `Providers:Delivery:BaseUrl`, `Providers:Delivery:ClientId/ClientSecret` (fake), `Providers:Delivery:WebhookSigningKey` (fake), `Providers:Payment:ApiKey`/`WebhookSigningKey` (Fase 5), `Providers:Delivery:ClientId`/`ClientSecret`/`WebhookSigningKey` (Fase 6; `BaseUrl`/`CustomerId` têm defaults), `Fulfillment:Origin:*` (endereço da loja fictícia, em `appsettings.json`), `Messaging:Sqs:*` (Fase 9), `OTEL_EXPORTER_OTLP_ENDPOINT`.
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
- Pedido fica `Created` (não `AwaitingPayment`): o simulator não está de pé ou `Providers:Payment:ApiKey` não bate — ver log 5101 da Api; a reconciliação do Worker recria o pagamento quando o provider voltar.
- Pedido fica `AwaitingPayment` para sempre: webhook não chegou (URL/chave do simulator) — ver `Simulator:Payments:WebhookUrl`/`WebhookSigningKey` e o log 5200 da Api; o Worker corrige em até `Worker:Reconciliation:PendingForSeconds`.
- Smoke rápido do fluxo de pagamento: valor com centavos `…99` recusa (pedido cancelado, estoque devolvido); `…98` aprova sem webhook (só a reconciliação resolve).
- Pedido `Paid` sem entrega: o Worker precisa estar rodando (`Worker:DeliveryRequests:IntervalSeconds`, 10 s); ver logs 6010–6015. CEP `00000-000` é recusado no checkout (400); CEP terminado em `001` força recotação (cotação de 1 s); `002` faz a entrega voltar (`returned`).
