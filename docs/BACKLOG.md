# BACKLOG — FulfillmentHub

Prioridades: **P0** (bloqueia o gate da fase), **P1** (necessário para o portfólio, mas não bloqueia o gate atual),
**P2** (melhoria valiosa), **P3** (nice-to-have). Regra: **nunca executar P2/P3 enquanto houver P0 aberto na fase atual.**
Status: `todo` · `doing` · `done` · `dropped`. IDs estáveis (`BL-xxx`) para referência em código (`// TODO(BL-042)`), commits e docs.

## Domain

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-001 | Tipos Common: `Entity`, `AggregateRoot`, `IDomainEvent`, `DomainException`, `Money`, `Address`, `EmailAddress`, `PhoneNumber`, IDs tipados (Guid v7) | 2 | P0 | done |
| BL-002 | `Product` com `Reserve/Release` e invariante de estoque | 2 | P0 | done |
| BL-003 | `Customer` com endereços (owned) | 2 | P0 | done |
| BL-004 | `Order` + `OrderItem` + máquina de estados + `OrderStatusChange` + eventos | 2 | P0 | done |
| BL-005 | `Payment` + `PaymentAttempt` + máquina de estados | 2 | P0 | done |
| BL-006 | `DeliveryQuote`, `Delivery` + `DeliveryEvent` + regra de ordem canônica | 2 | P0 | done |
| BL-007 | `User`, `Role` | 2 | P0 | done |
| BL-008 | Testes de unidade de todas as invariantes (DOMAIN.md §10) | 2 | P0 | done |
| BL-009 | Decidir D-P3 (taxa de entrega no pedido) e ajustar `Order`/`Payment` | 4 | P0 | todo |
| BL-010 | Número de pedido legível via sequence | 2 | P1 | done |

## API

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-020 | Esqueleto Minimal APIs, ProblemDetails, `IExceptionHandler`, OpenAPI + Scalar | 1 | P0 | done |
| BL-021 | Health checks `/health/live`, `/health/ready` | 1 | P0 | done |
| BL-022 | Correlation id middleware (`X-Correlation-Id`) | 1 | P0 | done |
| BL-023 | `POST /auth/login`, `GET /me` | 3 | P0 | todo |
| BL-024 | `POST /orders` com filtro `Idempotency-Key` + `IdempotencyRecord` | 4 | P0 | todo |
| BL-025 | `GET /orders/{id}`, `GET /orders` (keyset pagination), autorização por recurso (cliente só vê os seus) | 4 | P0 | todo |
| BL-026 | `POST /orders/{id}/cancel` | 4 | P0 | todo |
| BL-027 | `POST /webhooks/payments` (assinatura, dedup, 200 rápido) | 5 | P0 | todo |
| BL-028 | `POST /webhooks/deliveries` | 7 | P0 | todo |
| BL-029 | Endpoints de catálogo (Admin CRUD de produtos) | 4 | P1 | todo |
| BL-030 | Endpoints de operação: reprocessar outbox/webhook, reconciliar, cancelar entrega | 8–9 | P1 | todo |
| BL-031 | Validação nativa .NET 10 (`AddValidation`) nos requests | 4 | P0 | todo |
| BL-032 | Versionamento de API (`/v1`) — decidir se via path fixo | 4 | P2 | todo |
| BL-033 | Refresh tokens | 10 | P2 | todo |
| BL-034 | Arquivos `.http` de exemplo por módulo | 4+ | P2 | todo |

## Database

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-040 | `FulfillmentHubDbContext` + `IFulfillmentHubDbContext` + migration inicial + `dotnet ef` funcionando | 1 | P0 | done |
| BL-041 | Configurações EF por entidade; owned/complex types; conversores de ID; `xmin` como concurrency token | 2 | P0 | done |
| BL-042 | Constraints: `CHECK stock >= 0`, `UNIQUE` parciais (pagamento/entrega ativos por pedido), `UNIQUE(provider, provider_event_id)` | 2 | P0 | done |
| BL-043 | Índices para consultas de lista (customer_id+created_at, status), outbox (`status,next_attempt_at`), webhook | 2 | P0 | done |
| BL-044 | Seed de desenvolvimento por comando explícito (`dotnet run -- seed`) — movido da Fase 2: depende do `PasswordHasher` | 3 | P1 | todo |
| BL-045 | Interceptor de auditoria (`CreatedAt/UpdatedAt`) e `audit_logs` para ações operacionais | 2/8 | P1 | todo |
| BL-046 | Estratégia de aplicação de migrations em nuvem (task one-off / `migrations script --idempotent`) | 16 | P0 | todo |
| BL-047 | `ExecutionStrategy` (retry de transientes Npgsql) configurada e testada com transações explícitas | 8 | P1 | todo |

## Integrations

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-060 | Simulator de pagamento `/payments/v1` (contrato INTEGRATIONS §3, cenários, webhooks assinados) | 5 | P0 | todo |
| BL-061 | `IPaymentGatewayClient` typed client + resiliência + idempotência | 5 | P0 | todo |
| BL-062 | Simulator de entrega `/delivery/v1` (token, quote, create, get, cancel, erros, ciclo de vida, webhooks) | 6 | P0 | todo |
| BL-063 | `IDeliveryProviderClient` + pipeline (timeout/retry/CB) + matriz de retry testada | 6 | P0 | todo |
| BL-064 | Recotação em `expired_quote`; reconciliação em `409 duplicate_delivery` | 6 | P0 | todo |
| BL-065 | Cenários do simulator: latência, failure rate, timeout, 429+Retry-After, 503 couriers_busy, webhook duplicado/atrasado/fora de ordem, `SIM_FORCE_STATUS` | 6–7 | P0 | todo |
| BL-066 | Rota admin do simulator (`POST /admin/scenario`) | 7 | P1 | todo |
| BL-067 | Reconciliação periódica de pagamentos e entregas (`GET` no provider) | 5/8 | P1 | todo |
| BL-068 | Contract tests: schemas do simulator vs. DTOs do cliente | 12 | P1 | todo |
| BL-069 | Segundo provider de entrega (`AlternativeProvider`) para demonstrar substituição | 18+ | P3 | todo |
| BL-070 | Cache/renovação de token OAuth-like do simulator (401 → renovar 1x) | 6 | P1 | todo |

## Messaging

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-080 | `OutboxMessage` + interceptor de `SaveChanges` (eventos → outbox no mesmo commit) | 8 | P0 | todo |
| BL-081 | `OutboxPublisher` (`SKIP LOCKED`, lote, backoff+jitter, `Failed` após N) | 8 | P0 | todo |
| BL-082 | Handlers in-process: `OrderPlaced`→pagamento, `OrderPaid`→entrega, `OrderCancelled`→estoque/estorno | 8 | P0 | todo |
| BL-083 | Teste "crash entre commit e publicação não perde evento" | 8 | P0 | todo |
| BL-084 | `IMessagePublisher` SQS + filas/DLQ via LocalStack (compose init + Testcontainers) | 9 | P0 | todo |
| BL-085 | Consumidores SQS: long polling, visibility, concorrência limitada, `processed_messages` (idempotent consumer) | 9 | P0 | todo |
| BL-086 | Webhooks → `fh-webhooks-inbound` | 9 | P0 | todo |
| BL-087 | Métricas: outbox lag, DLQ count, idade da mensagem, retries | 9/11 | P0 | todo |
| BL-088 | Propagação de trace context (outbox → SQS attributes → consumer) | 11 | P0 | todo |
| BL-089 | Reprocessar DLQ pela Admin (redrive) | 17 | P1 | todo |

## Security

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-100 | user-secrets + `.env.example`; nenhum secret em `appsettings` | 1 | P0 | done |
| BL-101 | JWT (HS256, chave ≥ 32 bytes via secrets, exp curta), `FallbackPolicy` autenticado, policies por papel | 3 | P0 | todo |
| BL-102 | `PasswordHasher<T>` (PBKDF2) e política mínima de senha | 3 | P0 | todo |
| BL-103 | Rate limiting: login, webhooks, `POST /orders` | 3/10 | P0 | todo |
| BL-104 | Assinatura HMAC de webhooks (tempo constante) + tolerância de timestamp + limite de corpo | 5/7 | P0 | todo |
| BL-105 | Autorização por recurso (cliente só acessa os próprios pedidos) + testes de broken access control | 4/10 | P0 | todo |
| BL-106 | Security headers, CORS explícito, HTTPS redirection/HSTS (fora do dev) | 10 | P0 | todo |
| BL-107 | Threat model revisado com evidências; checklist OWASP Top 10 preenchido | 10 | P0 | todo |
| BL-108 | Redação de PII em logs (telefone/e-mail mascarados), sem corpo de webhook nos logs | 10 | P0 | todo |
| BL-109 | `dotnet list package --vulnerable` local; CodeQL + dependency review + gitleaks + Trivy em CI | 10/14 | P0 | todo |
| BL-110 | Secrets Manager na AWS (task role lê secrets; nada em env plano) | 15–16 | P0 | todo |
| BL-111 | SSRF: nenhuma URL vinda do usuário é chamada (webhook URL do simulator é configuração) — documentar | 10 | P1 | todo |

## Observability

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-120 | Logging JSON estruturado + `LoggerMessage` source generator + scopes com correlation id | 1 | P0 | done |
| BL-121 | OpenTelemetry traces/metrics (ASP.NET Core, HttpClient, Npgsql, runtime) → OTLP → Aspire Dashboard | 1 | P0 | done |
| BL-122 | `ActivitySource`/`Meter` do projeto; spans de casos de uso, provider calls, outbox, consumers | 5–11 | P0 | todo |
| BL-123 | Métricas de negócio/operação (OBSERVABILITY.md tabela) | 11 | P0 | todo |
| BL-124 | Instrumentação AWS SDK (SQS) | 9 | P1 | todo |
| BL-125 | Runbook de incidente executado localmente com evidências | 11 | P0 | todo |
| BL-126 | Alertas CloudWatch (5xx rate, p95, DLQ > 0, outbox lag, CB aberto) | 16 | P0 | todo |
| BL-127 | Teste: um pedido = um `trace_id` de ponta a ponta | 11 | P1 | todo |

## Tests

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-140 | Projetos de teste + xUnit v3 + Shouldly + Testcontainers + `WebApplicationFactory` base | 1 | P0 | done |
| BL-141 | ArchitectureTests (NetArchTest): dependências, convenções (`sealed`, sufixos) | 1 | P0 | done |
| BL-142 | Idempotência de `POST /orders` (3 cenários) | 4 | P0 | todo |
| BL-143 | Race condition de estoque (falha sem token, passa com) | 4 | P0 | todo |
| BL-144 | Webhook duplicado / atrasado / fora de ordem (pagamento e entrega) | 5/7 | P0 | todo |
| BL-145 | Matriz de retry com `HttpMessageHandler` fake; circuit breaker abre/fecha | 6 | P0 | todo |
| BL-146 | Outbox: perda zero, retry, `Failed` | 8 | P0 | todo |
| BL-147 | SQS: DLQ após N, consumidor idempotente | 9 | P0 | todo |
| BL-148 | E2E com compose: 3 fluxos | 12 | P0 | todo |
| BL-149 | Testes de autorização (401/403, acesso cruzado) | 3/10 | P0 | todo |
| BL-150 | Convergência com `SIM_FAILURE_RATE=0.3` | 12 | P1 | todo |
| BL-151 | Stryker (mutation) em Domain | 12 | P3 | todo |

## DevOps

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-160 | `docker-compose.yml` (Postgres 17, Aspire Dashboard; LocalStack na Fase 9; simulator na 6) | 1 | P0 | done |
| BL-161 | `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` | 1 | P0 | done |
| BL-162 | `git init` local, primeiro commit limpo (sem privados) | 1 | P0 | done |
| BL-163 | Dockerfiles multi-stage não-root + healthcheck (Api, Worker, Simulator, Admin) | 13 | P0 | todo |
| BL-164 | GitHub Actions: build/test/scan/image — **após autorização** | 14 | P0 | todo |
| BL-165 | Scripts `scripts/` (migrate, seed, run-e2e) | 12 | P1 | todo |

## AWS

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-180 | Terraform: VPC, subnets, SG, endpoints/NAT (decisão de custo) | 15 | P0 | todo |
| BL-181 | Terraform: ECR, ECS cluster, task definitions (Api, Worker, Simulator), Fargate services, ALB | 15 | P0 | todo |
| BL-182 | Terraform: RDS PostgreSQL (t4g.micro), SG restrito, parameter group | 15 | P0 | todo |
| BL-183 | Terraform: SQS + DLQ, IAM task roles least privilege, Secrets Manager, CloudWatch log groups/alarms, budget | 15 | P0 | todo |
| BL-184 | Deploy dev + migrations one-off + verificação E2E na nuvem + destroy | 16 | P0 | todo |
| BL-185 | ADOT collector sidecar → CloudWatch/X-Ray | 16 | P0 | todo |
| BL-186 | Documentar custos reais observados | 16 | P1 | todo |

## Documentation

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-200 | Fase 0: todos os docs base + ADRs + CLAUDE.md + skills | 0 | P0 | done |
| BL-201 | Atualizar DOMAIN.md/INTEGRATIONS.md conforme implementação (cada fase) | todas | P0 | todo |
| BL-202 | README do simulator com disclaimer | 6 | P0 | todo |
| BL-203 | Decidir idioma final da documentação (pt-BR vs. en) | 19 | P1 | todo |
| BL-204 | Diagramas (C4 nível 1–2, sequência) | 19 | P1 | todo |
| BL-205 | Guia de leitura para recrutador ("comece por aqui") | 19 | P1 | todo |

## UX/Admin

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-220 | Blazor Web App (server interactive) com autenticação por cookie/JWT e policies | 17 | P0 | todo |
| BL-221 | Listagens: pedidos, pagamentos, entregas, webhooks, outbox, DLQ, integrações (CB state, latência) | 17 | P0 | todo |
| BL-222 | Ações: reprocessar, cancelar entrega, reconciliar, redrive DLQ | 17 | P0 | todo |
| BL-223 | Busca por correlation id → linha do tempo do pedido | 17 | P1 | todo |

## Performance

| ID | Item | Fase | Pri | Status |
|---|---|---|---|---|
| BL-240 | Ferramenta de carga (k6 vs NBomber) — decidir | 18 | P0 | todo |
| BL-241 | Cenários: `POST /orders` concorrente, rajada de webhooks, simulator lento; medir p95/erros/lag | 18 | P0 | todo |
| BL-242 | Índices/keyset/compiled queries onde medido | 18 | P1 | todo |
| BL-243 | Bulkhead por provider se necessário (medir antes) | 18 | P2 | todo |
