# AWS_ARCHITECTURE — target architecture (Phases 15–16)

None of this exists yet. This document defines the **target**, the **cost trade-offs** and what will be learned by operating it.
A single cloud environment: `dev` (learning), created and destroyed on demand with Terraform. No commercial production.

## 1. Target diagram

```
                      Internet
                         │
                 ┌───────▼────────┐
                 │  ALB (HTTPS*)  │  * ACM cert if there is a domain; otherwise HTTP in dev (documented)
                 └───────┬────────┘
        ┌────────────────┼──────────────────┐
        │  VPC 10.0.0.0/16 (2 AZs)          │
        │  ┌───────── public subnets ─────┐ │
        │  │  ALB ENIs · (optional NAT)   │ │
        │  └──────────────────────────────┘ │
        │  ┌───────── private subnets ────┐ │
        │  │ ECS Fargate                  │ │
        │  │  ├─ fh-api (2 tasks) ◄──ALB  │ │
        │  │  ├─ fh-worker (1 task)       │ │
        │  │  └─ fh-simulator (1 task) ◄──┼─┼── only via ALB path /sim/* or the internal network (Cloud Map)
        │  │ RDS PostgreSQL (db.t4g.micro)│ │
        │  └──────────────────────────────┘ │
        │  VPC Endpoints: ECR (api+dkr), S3 (gw), CloudWatch Logs, Secrets Manager, SQS, X-Ray  ── or NAT Gateway
        └───────────────────────────────────┘
   SQS: fh-domain-events (+DLQ), fh-webhooks-inbound (+DLQ)
   Secrets Manager: fh/dev/db, fh/dev/jwt, fh/dev/webhooks
   CloudWatch: logs, metrics, alarms → SNS e-mail · X-Ray traces (via ADOT sidecar)
   ECR: fh-api, fh-worker, fh-simulator · IAM: least-privilege task roles · Budgets: monthly alarm
```

## 2. Services and rationale

| Service | Use | Why (and not something else) |
|---|---|---|
| **ECS on Fargate** | run Api, Worker, Simulator as services | no EC2/K8s to manage; scales per service; the target job description asked for ECS |
| **ALB** | HTTP(S) entry point, health checks, path routing | the standard for ECS; WAF optional later |
| **RDS PostgreSQL** | single database | managed (backup, patching); `db.t4g.micro` (Graviton, free-tier eligible on new accounts) |
| **SQS (standard) + DLQ** | messaging | serverless, cheap, integrates with IAM; FIFO is not needed (ordering is handled by state) |
| **Secrets Manager** | secrets | native injection into the task definition, rotation possible; Parameter Store for non-sensitive config (cheaper) |
| **CloudWatch + X-Ray (ADOT)** | logs, metrics, alarms, traces | native; OpenTelemetry keeps portability |
| **ECR** | images | scan on push |
| **IAM** | separate task roles (runtime) and execution role (pull/logs/secrets); least privilege per ARN | explicit requirement |
| **VPC** | isolation | 2 AZs (RDS Multi-AZ off in dev — cost) |
| **S3** | (future) proofs of delivery/reports; Terraform state (bucket + lock) | only when there is a use |

## 3. Cost decision: NAT Gateway vs VPC Endpoints vs public subnets (D-P6, to be decided in Phase 15)

| Option | Approx. cost/month (us-east-1) | Security | Note |
|---|---|---|---|
| A. Tasks in private subnets + **1 NAT Gateway** | ~US$ 32 + traffic | good | simplest; expensive for an idle dev environment |
| B. Private tasks + **VPC Endpoints** (ECR api, ECR dkr, Logs, Secrets, SQS, X-Ray ≈ 6 × ~US$ 7) + S3 gateway (free) | ~US$ 44 | best (no internet egress) | more expensive than A with few endpoints; no internet for the simulator/ADOT — fine |
| C. Tasks in **public subnets** with a public IP + restricted SG (inbound only from the ALB) | ~US$ 0 | acceptable for a learning dev environment; **not** for production | RDS stays private |

Proposal: **C in dev**, with option B documented as "production-like" and a Terraform module with an `enable_private_networking` flag to
demonstrate both (apply B for a few hours to validate, then destroy). Record the real observed cost.

## 4. Monthly cost estimate for the dev environment (running 24/7; destroy when not in use)

| Item | Estimate |
|---|---|
| ALB | ~US$ 16–20 |
| Fargate: api 2 × (0.25 vCPU, 0.5 GB) + worker 1 × + simulator 1 × | ~US$ 30–36 |
| RDS db.t4g.micro (20 GB gp3) | ~US$ 12–15 (or free tier) |
| SQS, Secrets (3 × US$ 0.40), CloudWatch (14-day logs, ~10 custom metrics, 8 alarms), ECR | ~US$ 5–10 |
| Network (option C) | ~US$ 0 |
| **Total** | **~US$ 65–80/month running; ~US$ 3/month stopped (ECR + secrets + state)** |

Controls: AWS Budgets with alerts at 50/80/100% of US$ 50; `terraform destroy` at the end of every cloud session; tags `Project=FulfillmentHub, Env=dev`.

## 5. Network and security

- VPC `/16`, 2 AZs; public `/24` subnets (ALB) and private `/24` subnets (RDS; tasks under option A/B).
- Security groups: `sg-alb` (443/80 from the internet), `sg-app` (inbound only from `sg-alb` on port 8080), `sg-db` (5432 only from `sg-app`), `sg-simulator` (8080 only from `sg-app`/`sg-alb`).
- No SSH/bastion: database access via ECS Exec in a one-off task (`aws ecs execute-command`) or the migration task.
- IAM: execution role (ECR pull, Logs, Secrets read per ARN); task roles: api (SQS SendMessage on the 2 queues, Secrets read), worker (SQS Receive/Delete/ChangeVisibility + DLQ, SendMessage), simulator (nothing beyond logs).
- Secrets: `fh/dev/db` (connection string), `fh/dev/jwt` (signing key), `fh/dev/webhooks` (HMAC keys). Never in the task's `environment`.

## 6. Deploy and operations (see DEPLOYMENT.md)

- Images: built in CI (or locally) → ECR with tag = commit SHA.
- Migrations: one-off `fh-migrate` task (same Api image with the `migrate` command) executed before the rollout; `--idempotent` script as an alternative.
- Rollout: ECS rolling update (min 100%/max 200%), `/health/ready` health check, ECS deployment circuit breaker on (automatic rollback).
- Scale: fixed in dev (2/1/1); document how autoscaling by CPU/`ApproximateNumberOfMessagesVisible` (worker) would work.
- Observability: ADOT sidecar → CloudWatch/X-Ray; alarms from OBSERVABILITY.md §6; one dashboard per service.

## 7. What will be learned by operating it (recorded in Phase 16)
Creating/destroying a VPC and ECS with Terraform; minimal IAM that actually works (real permission errors); RDS in a private subnet + one-off migration task; SQS with DLQ and redrive; alarms actually firing; observed vs estimated cost.

## 8. Environments
| Environment | Where | State |
|---|---|---|
| local | docker compose | Phase 1 |
| dev (cloud) | AWS, ephemeral | Phase 16 |
| staging | not planned (cost); only with a reason | — |
| commercial production | **does not exist** | — |
