# FulfillmentHub

Plataforma backend de **gestão e orquestração de pedidos, pagamentos e entregas**, construída em **C#/.NET 10** como
monólito modular — projeto de portfólio focado em engenharia backend: consistência (transactional outbox), idempotência,
concorrência, resiliência (retry/backoff/jitter/circuit breaker), mensageria (SQS), segurança, observabilidade
(OpenTelemetry) e operação (Docker, CI, AWS ECS/RDS via Terraform).

> **Status**: Fases 0–6 concluídas (fundação, domínio + banco, auth JWT, pedidos idempotentes, pagamentos com provider simulado + webhooks + reconciliação, entregas com provider simulado "Uber-like" + cotação no checkout). Próxima: Fase 7 (webhooks de entrega). 211 testes.
> Acompanhe em [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) e [docs/ROADMAP.md](docs/ROADMAP.md).

> **Disclaimer.** This project does not connect to Uber infrastructure or to any real payment provider.
> The simulators reproduce a limited subset of public API contracts for educational and portfolio purposes only.
> No real deliveries, charges, credentials or customers are involved. This is a personal learning/portfolio project,
> not a commercial or production system.

## Fluxo

```
Cliente → cria pedido → validação + reserva de estoque → pagamento (provider simulado) → confirmação via webhook
→ cotação e solicitação de entrega (provider simulado "Uber-like") → webhooks de status → pedido em estado final
```

## Stack

C# 14 · ASP.NET Core 10 (Minimal APIs) · EF Core 10 + PostgreSQL · xUnit v3 + Testcontainers · OpenTelemetry · AWS SQS (LocalStack local) ·
Docker Compose · GitHub Actions (futuro) · Terraform → AWS ECS Fargate, RDS, SQS, Secrets Manager, CloudWatch (futuro) · Blazor (admin, futuro).

## Estrutura (planejada)

```
src/  Domain · Application · Infrastructure · Api · Worker · ProviderSimulator · Admin
tests/ UnitTests · IntegrationTests · ArchitectureTests · E2ETests
docs/  documentação técnica, roadmap, backlog, ADRs
```

## Documentação

Índice em [docs/README.md](docs/README.md). Comece por [docs/PRODUCT.md](docs/PRODUCT.md) e [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Licença

A definir antes da publicação (provavelmente MIT).
