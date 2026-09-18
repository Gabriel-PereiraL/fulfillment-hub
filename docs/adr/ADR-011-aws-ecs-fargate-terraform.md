# ADR-011 — AWS: ECS Fargate + ALB + RDS + SQS + Secrets Manager + CloudWatch, via Terraform

**Status**: aceita · **Data**: 2026-09-18 (implementação: Fases 15–16, mediante autorização de custo)

## Contexto
O feedback do processo seletivo pediu profundidade em ECS, RDS, IAM, VPC e IaC. O projeto precisa de um ambiente de nuvem
real para aprender operando, com custo controlado e sem produção comercial.

## Opções
- Compute: **ECS Fargate** vs. ECS EC2 vs. EKS vs. App Runner vs. Lambda.
- Banco: **RDS PostgreSQL** vs. Aurora Serverless v2 vs. Postgres em container.
- IaC: **Terraform** vs. CDK (C#) vs. CloudFormation/SAM.
- Rede: privada + NAT vs. privada + VPC endpoints vs. pública com SG (D-P6).

## Decisão
ECS Fargate (Api, Worker, Simulator como serviços), ALB, RDS PostgreSQL `db.t4g.micro` (single-AZ em dev), SQS + DLQ,
Secrets Manager (+ Parameter Store para config), CloudWatch (logs/métricas/alarmes) + X-Ray via ADOT, ECR, IAM task roles por serviço,
AWS Budgets. Terraform com módulos (`network`, `ecs-service`, `rds`, `sqs`, `secrets`, `observability`, `iam`) e ambiente `envs/dev`.

## Motivo
- Fargate: sem gerenciar hosts; é o serviço pedido; mapeia 1:1 com os processos do monólito.
- Terraform: padrão de mercado e pedido explicitamente (CDK em C# seria coerente com a stack, mas Terraform tem mais alcance em vagas).
- RDS: gerenciado; Aurora Serverless é mais caro no mínimo; container não ensina RDS.

## Trade-offs
- Custo (~US$ 65–80/mês ligado; ~US$ 3 desligado): mitigado com `destroy` após cada sessão e budget alarm.
- Single-AZ/RDS pequeno em dev: não é "production-like" e o README dirá isso.
- Rede: decisão D-P6 pendente (subnets públicas em dev vs. endpoints) — a opção escolhida será documentada com custo real.

## Consequências
- AWS_ARCHITECTURE.md e DEPLOYMENT.md descrevem o alvo; nada é criado sem autorização explícita do usuário.
- Aprendizados reais (permissões IAM que falharam, custos observados) serão registrados na Fase 16.
