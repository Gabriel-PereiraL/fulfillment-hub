# ADR-011 — AWS: ECS Fargate + ALB + RDS + SQS + Secrets Manager + CloudWatch, via Terraform

**Status**: accepted · **Date**: 2026-09-18 (implementation: Phases 15–16, subject to cost authorization)

## Context
Feedback from a hiring process asked for depth in ECS, RDS, IAM, VPC and IaC. The project needs a real cloud environment
to learn by operating it, with controlled cost and no commercial production.

## Options
- Compute: **ECS Fargate** vs. ECS on EC2 vs. EKS vs. App Runner vs. Lambda.
- Database: **RDS PostgreSQL** vs. Aurora Serverless v2 vs. Postgres in a container.
- IaC: **Terraform** vs. CDK (C#) vs. CloudFormation/SAM.
- Network: private + NAT vs. private + VPC endpoints vs. public with SG (D-P6).

## Decision
ECS Fargate (Api, Worker, Simulator as services), ALB, RDS PostgreSQL `db.t4g.micro` (single-AZ in dev), SQS + DLQ,
Secrets Manager (+ Parameter Store for config), CloudWatch (logs/metrics/alarms) + X-Ray via ADOT, ECR, IAM task roles per service,
AWS Budgets. Terraform with modules (`network`, `ecs-service`, `rds`, `sqs`, `secrets`, `observability`, `iam`) and an `envs/dev` environment.

## Rationale
- Fargate: no hosts to manage; it is the requested service; maps 1:1 to the monolith's processes.
- Terraform: the market standard and explicitly requested (CDK in C# would be coherent with the stack, but Terraform has more reach in job postings).
- RDS: managed; Aurora Serverless is more expensive at the minimum; a container does not teach RDS.

## Trade-offs
- Cost (~US$ 65–80/month running; ~US$ 3 stopped): mitigated with `destroy` after every session and a budget alarm.
- Single-AZ/small RDS in dev: not "production-like" and the README will say so.
- Network: decision D-P6 pending (public subnets in dev vs. endpoints) — the chosen option will be documented with the real cost.

## Consequences
- AWS_ARCHITECTURE.md and DEPLOYMENT.md describe the target; nothing is created without the user's explicit authorization.
- Real lessons (IAM permissions that failed, observed costs) will be recorded in Phase 16.
