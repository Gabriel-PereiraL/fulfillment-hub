# FulfillmentHub

Backend de **orquestração de pedidos, pagamentos e entregas** em **C# 14 / .NET 10**, construído como monólito modular
para demonstrar práticas de engenharia backend orientadas a problemas reais: idempotência, concorrência, consistência
entre banco e efeitos externos (transactional outbox), mensageria com consumidores idempotentes, integrações resilientes,
webhooks assinados, segurança e observabilidade — tudo coberto por testes que provam o comportamento.

> This is an engineering portfolio project built to demonstrate production-oriented backend practices in C#/.NET.
> It is not a commercial product and does not represent a real logistics/payment operation.

**Status (2026-09-18):** fases 0–9 de 20 concluídas. Um pedido percorre `Created → AwaitingPayment → Paid →
DeliveryRequested → InDelivery → Delivered` de ponta a ponta, com os efeitos externos saindo pela outbox e por filas SQS
(LocalStack) e os providers respondendo por webhooks assinados. Próxima fase: **10 — Security hardening**.
Estado detalhado em [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md); plano em [docs/ROADMAP.md](docs/ROADMAP.md).

> **Disclaimer.** This project does not connect to Uber infrastructure or to any real payment provider.
> The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.
> No real deliveries, charges, credentials or customers are involved.

## Fluxo

```
POST /orders ──► reserva de estoque (xmin) + cotação de entrega + OrderPlaced na outbox   [mesma transação]
      │
      ▼ Worker publica a outbox no SQS (fh-domain-events) e consome com deduplicação
OrderPlaced ──► pagamento criado no provider simulado ──► webhook HMAC "paid" ──► Paid (+ OrderPaid)
OrderPaid   ──► entrega criada no provider simulado "Uber-like" ──► webhooks de status ──► InDelivery → Delivered
OrderCancelled / PaymentPaid tardio ──► estorno automático
Reconciliação periódica cobre webhooks perdidos; DLQ + admin cobrem mensagens que falham.
```

## Implementado

- **Pedidos**: `POST /orders` idempotente por `Idempotency-Key` (replay da resposta, 422 em payload divergente, 409 em
  andamento), reserva de estoque com concorrência otimista (`xmin`) provada por teste de 20 compradores concorrentes,
  cotação da entrega no checkout com taxa estimada como fallback, cancelamento com devolução de estoque, paginação keyset.
- **Autenticação/autorização**: JWT HS256 (`JsonWebTokenHandler`), `PasswordHasher` nativo, policies por papel,
  deny-by-default, rate limit de login, falhas de login indistinguíveis, autorização por recurso (cliente só vê o que é seu).
- **Pagamentos**: provider simulado com contrato próprio; typed `HttpClient` com `Microsoft.Extensions.Http.Resilience`
  (timeout total → retry exponencial com jitter e `Retry-After` → circuit breaker → timeout por tentativa); webhooks com
  HMAC-SHA256 em tempo constante, tolerância de timestamp, inbox com deduplicação, verificação `GET` antes de aplicar
  `paid`; reconciliação de pagamentos pendentes; estorno automático de pedidos cancelados após captura.
- **Entregas**: simulador que reproduz um subconjunto documentado do contrato público Uber Direct (token, quote, create,
  get, cancel, `event.delivery_status`); token cache com renovação; recotação em `expired_quote`; `409 duplicate_delivery`
  reconciliado adotando a entrega existente; eventos fora de ordem/duplicados/atrasados registrados sem regredir estado;
  reconciliação de entregas silenciosas.
- **Transactional outbox**: eventos de domínio gravados no mesmo `SaveChanges` do agregado; publisher com
  `FOR UPDATE SKIP LOCKED` + lease, backoff exponencial, `Failed` após N tentativas, endpoints admin para listar e
  reprocessar; nenhum efeito externo dentro de um request HTTP (exceções documentadas).
- **Mensageria (SQS)**: filas `fh-domain-events` e `fh-webhooks-inbound` com dead-letter queues e redrive policy,
  consumidores com long polling, concorrência limitada, backoff por visibilidade, deduplicação persistida na mesma
  transação do efeito; LocalStack no compose; modo sem broker (in-process) por configuração.
- **Observabilidade**: logs estruturados com `LoggerMessage` (sem PII/segredos), OpenTelemetry (ASP.NET Core,
  HttpClient, Npgsql, AWS SDK, runtime), spans próprios para providers/outbox/filas com propagação de trace pela fila,
  métricas de negócio e operação, correlation id, Aspire Dashboard local como UI de OTLP.
- **Testes**: 225 (125 unit, 5 architecture, 95 integration) — os de integração sobem a API, o Worker e o simulador
  in-process contra PostgreSQL e LocalStack reais via Testcontainers, com webhooks trafegando entre os hosts.

## Stack

C# 14 · .NET 10 · ASP.NET Core Minimal APIs (validação nativa, ProblemDetails, OpenAPI + Scalar em dev) · EF Core 10 +
Npgsql/PostgreSQL 17 · `Microsoft.Extensions.Http.Resilience` (Polly v8) · AWS SDK for .NET (SQS) + LocalStack ·
OpenTelemetry · xUnit v3 + Shouldly + Testcontainers + NetArchTest · Docker Compose.

Sem MediatR, AutoMapper, FluentValidation, repositórios genéricos ou frameworks de mensageria: cada decisão está
justificada em [docs/DECISIONS.md](docs/DECISIONS.md) e nos [ADRs](docs/adr/).

## Estrutura

```
src/
  FulfillmentHub.Domain            agregados, value objects, máquinas de estado, eventos de domínio
  FulfillmentHub.Application       casos de uso, ports (providers, mensageria), handlers da outbox, métricas
  FulfillmentHub.Infrastructure    EF Core + migrations, outbox, SQS, clientes HTTP dos providers, JWT, telemetria
  FulfillmentHub.Api               Minimal APIs, auth, idempotência, webhooks, admin
  FulfillmentHub.Worker            publisher da outbox, consumidores SQS, reconciliações, varreduras
  FulfillmentHub.ProviderSimulator providers simulados de pagamento e entrega (host separado)
tests/
  FulfillmentHub.UnitTests         domínio, clientes de provider contra transporte scriptado
  FulfillmentHub.IntegrationTests  API + Worker + simulador in-process, PostgreSQL e LocalStack (Testcontainers)
  FulfillmentHub.ArchitectureTests dependências entre camadas
docs/                               produto, arquitetura, domínio, integrações, segurança, observabilidade, ADRs
```

## Engenharia demonstrada

| Tema | Onde olhar |
|---|---|
| Idempotência de API (replay, 422, 409 concorrente) | `Api/Idempotency`, `IntegrationTests/Orders/PlaceOrderTests` |
| Concorrência otimista sob carga | `PlaceOrderHandler`, `PlaceOrder_TwentyBuyersForTheLastUnit_ExactlyOneSucceeds` |
| Retry/backoff/jitter/circuit breaker (abre **e** fecha) | `Infrastructure/Providers`, `UnitTests/Payments`, `UnitTests/Deliveries` |
| Webhooks HMAC, inbox, fora de ordem, reconciliação | `Api/Webhooks`, `Infrastructure/Webhooks`, `IntegrationTests/Payments`, `IntegrationTests/Deliveries` |
| Transactional outbox (commit atômico, retry, DLQ lógica, requeue) | `Infrastructure/Outbox`, `IntegrationTests/Outbox` |
| SQS: consumidor idempotente e dead-letter | `Worker/Messaging`, `IntegrationTests/Messaging` |
| Segurança (JWT, hashing, deny-by-default, rate limit) | `Api/Identity`, [docs/SECURITY.md](docs/SECURITY.md) |
| Observabilidade | `Infrastructure/Telemetry`, [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md) |

## Roadmap (ainda não implementado)

Security hardening (headers, CORS, checklist OWASP com evidências) → observability hardening (runbooks, alertas) →
testing hardening (E2E, caos) → imagens Docker e compose completo → CI (GitHub Actions) → IaC (Terraform) e deploy na
AWS (ECS Fargate, RDS, SQS) → Admin/Ops UI (Blazor) → testes de performance/resiliência → release do portfólio.
Detalhes e critérios de aceite por fase em [docs/ROADMAP.md](docs/ROADMAP.md).

## Documentação

Índice em [docs/README.md](docs/README.md). Comece por [PRODUCT.md](docs/PRODUCT.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md)
e [DOMAIN.md](docs/DOMAIN.md); integrações e o contrato "real vs. simulado" em [INTEGRATIONS.md](docs/INTEGRATIONS.md);
decisões em [DECISIONS.md](docs/DECISIONS.md) e [adr/](docs/adr/).

## Executando localmente

Pré-requisitos: .NET SDK 10, Docker Desktop. Passo a passo completo em [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

```bash
cp .env.example .env                        # senha local do Postgres (não é um segredo real)
docker compose --profile deps up -d         # PostgreSQL, LocalStack (SQS) e Aspire Dashboard
dotnet tool restore
# segredos de desenvolvimento ficam em user-secrets, nunca em arquivos versionados:
dotnet user-secrets set "Database:ConnectionString" "Host=localhost;Port=5432;Database=fulfillmenthub;Username=fh;Password=<POSTGRES_PASSWORD>" --project src/FulfillmentHub.Api
dotnet user-secrets set "Jwt:SigningKey" "<64 chars aleatórios>" --project src/FulfillmentHub.Api
# (demais chaves — senhas do seed, credenciais dev-only dos simuladores — listadas em docs/DEVELOPMENT.md)
dotnet ef database update --project src/FulfillmentHub.Infrastructure --startup-project src/FulfillmentHub.Api
dotnet run --project src/FulfillmentHub.Api -- seed      # dados fictícios de desenvolvimento
dotnet run --project src/FulfillmentHub.ProviderSimulator # http://localhost:5100
dotnet run --project src/FulfillmentHub.Worker
dotnet run --project src/FulfillmentHub.Api               # http://localhost:5000/scalar/v1
```

## Testes

```bash
dotnet test --solution FulfillmentHub.slnx   # 225 testes; Docker necessário para os de integração
```

## Disclaimer

Os providers de pagamento e entrega são **simulados** (`FulfillmentHub.ProviderSimulator`). O de entrega reproduz um
subconjunto documentado do contrato público da Uber Direct API apenas para fins educacionais; o de pagamento tem um
contrato próprio inspirado no ciclo de vida comum de PSPs. Nada aqui se conecta a infraestrutura real, movimenta dinheiro
ou envolve clientes reais.

## License

[MIT](LICENSE) © 2026 Gabriel Vitor Pereira Leite
