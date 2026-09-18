# DEVELOPMENT — developer guide

## 1. Prerequisites (reference versions verified on 2026-09-18)

| Tool | Reference version | Required |
|---|---|---|
| .NET SDK | 10.0.400 (runtime 10.0.11) — LTS | 10.0.x |
| Git | 2.54.0.windows.1 | any recent |
| Docker Engine / Compose | 29.4.3 / v5.1.3 | Docker Desktop with WSL2 running (Testcontainers) |
| Terraform | **not installed** | only in Phase 15 (install via winget/choco) |
| AWS CLI | **not installed** | only in Phase 15 (install; use SSO/temporary credentials) |
| OS | Windows 11 Pro | shell: PowerShell 5.1 or Git Bash |

## 2. First setup

```bash
cd FulfillmentHub
dotnet --version                       # 10.0.x
cp .env.example .env                   # set POSTGRES_PASSWORD (local value; .env is ignored by Git)
docker compose --profile deps up -d    # postgres (5432) + localstack SQS (4566) + aspire-dashboard (UI 18888, OTLP gRPC 4317)
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<same as .env>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<same as .env>" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Jwt:SigningKey" "<64 random chars, e.g. openssl rand -base64 48>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Seed:AdminPassword" "<dev password, 12+ chars>" --project src/FulfillmentHub.Api      # same for Seed:OperatorPassword and Seed:CustomerPassword
# Payment provider (Phase 5): the values must match src/FulfillmentHub.ProviderSimulator/appsettings.Development.json (dev-only, not real secrets)
dotnet user-secrets set "Providers:Payment:ApiKey" "dev-only-payment-api-key" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Payment:WebhookSigningKey" "dev-only-payment-webhook-signing-key" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Payment:ApiKey" "dev-only-payment-api-key" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Providers:Payment:WebhookSigningKey" "dev-only-payment-webhook-signing-key" --project src/FulfillmentHub.Worker
# Delivery provider (Phase 6): likewise, dev-only values from src/FulfillmentHub.ProviderSimulator/appsettings.Development.json (Simulator:Delivery)
dotnet user-secrets set "Providers:Delivery:ClientId" "dev-only-delivery-client-id" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Delivery:ClientSecret" "dev-only-delivery-client-secret" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Delivery:WebhookSigningKey" "dev-only-delivery-webhook-signing-key" --project src/FulfillmentHub.Api
dotnet user-secrets set "Providers:Delivery:ClientId" "dev-only-delivery-client-id" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Providers:Delivery:ClientSecret" "dev-only-delivery-client-secret" --project src/FulfillmentHub.Worker
dotnet user-secrets set "Providers:Delivery:WebhookSigningKey" "dev-only-delivery-webhook-signing-key" --project src/FulfillmentHub.Worker
dotnet tool restore                    # dotnet-ef (manifest in .config/dotnet-tools.json)
dotnet ef database update --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet run --project src/FulfillmentHub.Api -- seed       # fictional development data (Development only; users admin@/operator@/customer.local)
dotnet run --project src/FulfillmentHub.Api               # http://localhost:5000 — /scalar/v1 (OpenAPI UI), /health/live, /health/ready
dotnet run --project src/FulfillmentHub.Worker
dotnet run --project src/FulfillmentHub.ProviderSimulator # http://localhost:5100/health/live — sends webhooks to http://localhost:5000/api/v1/webhooks/payments
```

Aspire Dashboard: http://localhost:18888 (traces/logs/metrics). The hosts export OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (it already is in `appsettings.Development.json`).

SQS (Phase 9): in Development `Messaging:Sqs:Enabled=true` points at the compose LocalStack (`http://localhost:4566`, placeholder credentials `test`/`test` — not secrets). The Worker creates the `fh-domain-events`/`fh-webhooks-inbound` queues (+ `-dlq`) on startup; inspect them with `docker exec fulfillmenthub-localstack-1 awslocal sqs list-queues`. Without LocalStack, set `Messaging__Sqs__Enabled=false`: the outbox dispatches in-process and webhooks are processed inside the request (same behaviour, no queue).

## 3. Day-to-day commands

```bash
dotnet build FulfillmentHub.slnx                        # TreatWarningsAsErrors + analyzers
dotnet test --solution FulfillmentHub.slnx              # unit + architecture + integration (Docker required)
dotnet test --project tests/FulfillmentHub.UnitTests    # a single project
dotnet format FulfillmentHub.slnx --verify-no-changes   # style (.editorconfig)
dotnet ef migrations add <Name> --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent -o artifacts/migrate.sql --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet list FulfillmentHub.slnx package --vulnerable --include-transitive
```

The tests use the Microsoft.Testing.Platform (`global.json` → `test.runner`): use `--solution`/`--project`, not the positional path.

## 4. Configuration and secrets

- `appsettings.json` (defaults without secrets) → `appsettings.Development.json` (ports, log levels) → **user-secrets** (dev) → environment variables (containers/AWS).
- Never commit a secret. `.env.example` documents the compose variables. Expected keys: `Database:ConnectionString` (Phase 1), `Jwt:SigningKey` (secret, ≥ 32 chars; `Jwt:Issuer`/`Audience` have defaults in appsettings), `Seed:AdminPassword`/`OperatorPassword`/`CustomerPassword` (only for the `seed` command), `Providers:Payment:ApiKey`/`WebhookSigningKey` (Phase 5), `Providers:Delivery:ClientId`/`ClientSecret`/`WebhookSigningKey` (Phase 6; `BaseUrl`/`CustomerId` have defaults), `Fulfillment:Origin:*` (the fictional store's address, in `appsettings.json`), `Messaging:Sqs:*` (Phase 9), `OTEL_EXPORTER_OTLP_ENDPOINT`.
- Typed options with `ValidateOnStart`: the application **does not start** with invalid configuration — this is intentional.

## 5. Code conventions
See `.editorconfig` (Phase 1). Public summary: C# 14, nullable enabled, warnings as errors, file-scoped namespaces, `sealed` by default, records for DTOs, entities with behaviour, `CancellationToken` in every asynchronous method, `TimeProvider` for time, no `.Result/.Wait()`.

## 6. Workflow per task
1. Read `PROJECT_STATE.md` and pick the `BACKLOG.md` item (P0 of the current phase).
2. Implement in small slices; build and tests green after each slice.
3. Tests per `TEST_STRATEGY.md` (matrix).
4. Update the affected docs + `PROJECT_STATE.md`.
5. Local commit (`feat:`, `fix:`, `docs:`, `test:`, `chore:`, `refactor:`), after checking `git status` for secrets or local files.

## 7. Folder structure (see ARCHITECTURE.md §2) and where things go
- New business rule → `Domain/<Module>/`, test in `UnitTests/<Module>/`.
- New endpoint → `Api/<Module>/<Module>Endpoints.cs` + use case in `Application/<Module>/` + test in `IntegrationTests/<Module>/`.
- New table → entity + `Infrastructure/Persistence/Configurations/<Module>/` + migration.
- New external call → port in `Application/<Module>/`, adapter in `Infrastructure/Providers/`, policy in `Infrastructure/Providers/Resilience`.

## 8. Troubleshooting (grows with each phase)
- Testcontainers on Windows: Docker Desktop must be running with the WSL2 backend; the first run pulls images (slow).
- `dotnet ef` not found: `dotnet tool restore`.
- Application does not start because of invalid Options: read the validation message; configure user-secrets.
- Order stays `Created` (not `AwaitingPayment`): the simulator is not running or `Providers:Payment:ApiKey` does not match — see the Api log 5101; the Worker's reconciliation recreates the payment when the provider comes back.
- Order stays `AwaitingPayment` forever: the webhook never arrived (simulator URL/key) — check `Simulator:Payments:WebhookUrl`/`WebhookSigningKey` and the Api log 5200; the Worker corrects it within `Worker:Reconciliation:PendingForSeconds`.
- Quick smoke test of the payment flow: an amount with cents `…99` is declined (order cancelled, stock returned); `…98` is approved without a webhook (only reconciliation resolves it).
- Order `Paid` without a delivery: the Worker must be running (`Worker:DeliveryRequests:IntervalSeconds`, 10 s); see logs 6010–6015. Postal code `00000-000` is refused at checkout (400); a postal code ending in `001` forces a requote (1-second quote); `002` makes the delivery come back (`returned` → order cancelled, stock returned); `003` delivers without webhooks (the order only advances when the Worker's reconciliation runs, `Worker:DeliveryReconciliation:QuietForSeconds` = 300).
- Order stuck in `DeliveryRequested` while the simulator advances: `Simulator:Delivery:WebhookUrl` must point at `http://localhost:5000/api/v1/webhooks/deliveries` and `Providers:Delivery:WebhookSigningKey` (user-secrets) must match `Simulator:Delivery:WebhookSigningKey`; see the Api log 5200 (signature rejected) and the simulator log 9001 (webhook delivery failed).
