# AWS_ARCHITECTURE — arquitetura alvo (Fases 15–16)

Nada disto existe ainda. Este documento define o **alvo**, os **trade-offs de custo** e o que será aprendido operando.
Ambiente único de nuvem: `dev` (aprendizado), criado e destruído sob demanda com Terraform. Sem produção comercial.

## 1. Diagrama alvo

```
                      Internet
                         │
                 ┌───────▼────────┐
                 │  ALB (HTTPS*)  │  * ACM cert se houver domínio; senão HTTP em dev (documentado)
                 └───────┬────────┘
        ┌────────────────┼──────────────────┐
        │  VPC 10.0.0.0/16 (2 AZs)          │
        │  ┌───────── public subnets ─────┐ │
        │  │  ALB ENIs · (NAT opcional)   │ │
        │  └──────────────────────────────┘ │
        │  ┌───────── private subnets ────┐ │
        │  │ ECS Fargate                  │ │
        │  │  ├─ fh-api (2 tasks) ◄──ALB  │ │
        │  │  ├─ fh-worker (1 task)       │ │
        │  │  └─ fh-simulator (1 task) ◄──┼─┼── só via ALB path /sim/* ou rede interna (Cloud Map)
        │  │ RDS PostgreSQL (db.t4g.micro)│ │
        │  └──────────────────────────────┘ │
        │  VPC Endpoints: ECR (api+dkr), S3 (gw), CloudWatch Logs, Secrets Manager, SQS, X-Ray  ── ou NAT Gateway
        └───────────────────────────────────┘
   SQS: fh-domain-events (+DLQ), fh-webhooks-inbound (+DLQ)
   Secrets Manager: fh/dev/db, fh/dev/jwt, fh/dev/webhooks
   CloudWatch: logs, metrics, alarms → SNS e-mail · X-Ray traces (via ADOT sidecar)
   ECR: fh-api, fh-worker, fh-simulator · IAM: task roles least privilege · Budgets: alarme mensal
```

## 2. Serviços e justificativas

| Serviço | Uso | Por quê (e não outra coisa) |
|---|---|---|
| **ECS on Fargate** | rodar Api, Worker, Simulator como serviços | sem gerenciar EC2/K8s; escala por serviço; a vaga pediu ECS |
| **ALB** | entrada HTTP(S), health checks, roteamento por path | padrão para ECS; WAF opcional depois |
| **RDS PostgreSQL** | banco único | gerenciado (backup, patch); `db.t4g.micro` (Graviton, elegível a free tier em contas novas) |
| **SQS (standard) + DLQ** | mensageria | serverless, barato, integra com IAM; FIFO não é necessário (ordem tratada por estado) |
| **Secrets Manager** | segredos | injeção nativa na task definition, rotação possível; Parameter Store para config não sensível (mais barato) |
| **CloudWatch + X-Ray (ADOT)** | logs, métricas, alarmes, traces | nativo; OpenTelemetry mantém portabilidade |
| **ECR** | imagens | scan on push |
| **IAM** | task roles (runtime) e execution role (pull/logs/secrets) separados; least privilege por ARN | requisito explícito |
| **VPC** | isolamento | 2 AZs (RDS Multi-AZ desligado em dev — custo) |
| **S3** | (futuro) provas de entrega/relatórios; estado do Terraform (bucket + lock) | só quando houver uso |

## 3. Decisão de custo: NAT Gateway vs VPC Endpoints vs subnets públicas (D-P6, decidir na Fase 15)

| Opção | Custo aprox./mês (us-east-1) | Segurança | Nota |
|---|---|---|---|
| A. Tasks em subnets privadas + **1 NAT Gateway** | ~US$ 32 + tráfego | boa | mais simples; caro para dev ocioso |
| B. Tasks privadas + **VPC Endpoints** (ECR api, ECR dkr, Logs, Secrets, SQS, X-Ray ≈ 6 × ~US$ 7) + S3 gateway (grátis) | ~US$ 44 | melhor (sem saída para internet) | mais caro que A se poucos endpoints; sem internet para o simulator/ADOT — ok |
| C. Tasks em **subnets públicas** com IP público + SG restrito (inbound só do ALB) | ~US$ 0 | aceitável para dev de aprendizado; **não** para produção | RDS continua privado |

Proposta: **C em dev**, com a opção B documentada como "production-like" e um módulo Terraform com flag `enable_private_networking` para
demonstrar as duas (aplicar B por algumas horas para validar e destruir). Registrar custo real observado.

## 4. Estimativa de custo mensal do ambiente dev (ligado 24/7; destruir quando não usar)

| Item | Estimativa |
|---|---|
| ALB | ~US$ 16–20 |
| Fargate: api 2 × (0.25 vCPU, 0.5 GB) + worker 1 × + simulator 1 × | ~US$ 30–36 |
| RDS db.t4g.micro (20 GB gp3) | ~US$ 12–15 (ou free tier) |
| SQS, Secrets (3 × US$ 0.40), CloudWatch (logs 14 d, ~10 métricas custom, 8 alarmes), ECR | ~US$ 5–10 |
| Rede (opção C) | ~US$ 0 |
| **Total** | **~US$ 65–80/mês ligado; ~US$ 3/mês desligado (ECR + secrets + estado)** |

Controles: AWS Budgets com alerta em 50/80/100% de US$ 50; `terraform destroy` ao fim de cada sessão de nuvem; tags `Project=FulfillmentHub, Env=dev`.

## 5. Rede e segurança

- VPC `/16`, 2 AZs; subnets públicas `/24` (ALB) e privadas `/24` (RDS; tasks se opção A/B).
- Security groups: `sg-alb` (443/80 da internet), `sg-app` (inbound só de `sg-alb` na porta 8080), `sg-db` (5432 só de `sg-app`), `sg-simulator` (8080 só de `sg-app`/`sg-alb`).
- Sem SSH/bastion: acesso ao banco via ECS Exec em task one-off (`aws ecs execute-command`) ou task de migração.
- IAM: execution role (ECR pull, Logs, Secrets read por ARN); task roles: api (SQS SendMessage nas 2 filas, Secrets read), worker (SQS Receive/Delete/ChangeVisibility + DLQ, SendMessage), simulator (nada além de logs).
- Secrets: `fh/dev/db` (connection string), `fh/dev/jwt` (signing key), `fh/dev/webhooks` (HMAC keys). Nunca em `environment` da task.

## 6. Deploy e operação (ver DEPLOYMENT.md)

- Imagens: build no CI (ou local) → ECR com tag = SHA do commit.
- Migrations: task one-off `fh-migrate` (mesma imagem da Api com comando `migrate`) executada antes do rollout; script `--idempotent` como alternativa.
- Rollout: ECS rolling update (min 100%/max 200%), health check `/health/ready`, circuit breaker de deployment do ECS ligado (rollback automático).
- Escala: dev fixo (2/1/1); documentar como seria autoscaling por CPU/`ApproximateNumberOfMessagesVisible` (worker).
- Observabilidade: ADOT sidecar → CloudWatch/X-Ray; alarmes de OBSERVABILITY.md §6; dashboard por serviço.

## 7. O que será aprendido operando (registro na Fase 16)
Criar/destruir VPC e ECS com Terraform; IAM mínimo que realmente funciona (erros de permissão reais); RDS em subnet privada + task one-off de migração; SQS com DLQ e redrive; alarmes disparando de verdade; custo observado vs estimado.

## 8. Ambientes
| Ambiente | Onde | Estado |
|---|---|---|
| local | docker compose | Fase 1 |
| dev (cloud) | AWS, efêmero | Fase 16 |
| staging | não planejado (custo); só se houver motivo | — |
| produção comercial | **não existe** | — |
