# DEPLOYMENT — FulfillmentHub

How the system is packaged, published and operated per environment. Phases 13 (Docker), 14 (CI), 15 (Terraform), 16 (cloud).
Written in Phase 0 (nothing executed externally). Since 2026-09-18 the code is published at https://github.com/Gabriel-PereiraL/fullfillmentHub (manual push); CI/CD, images and AWS remain future work (Phases 13–16).

## 1. Environments

| Environment | Infra | Database | Queue | Secrets | Observability | How it starts |
|---|---|---|---|---|---|---|
| local | docker compose | Postgres 17 container | LocalStack SQS (Phase 9) | user-secrets / `.env` | Aspire Dashboard | `docker compose up -d` + `dotnet run` (or everything in containers in Phase 13) |
| dev (cloud) | ECS Fargate + ALB | RDS PostgreSQL | SQS | Secrets Manager | CloudWatch + X-Ray | Terraform + CI/documented manual deploy |

## 2. Docker images (Phase 13)

- Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` (build/test/publish) → `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime); `USER app` (non-root, port 8080); `HEALTHCHECK` calling `/health/live`.
- One image per host: `fh-api`, `fh-worker`, `fh-simulator`, `fh-admin`. Tag = short commit SHA + `latest` only in dev.
- No secrets in build args/layers; `.dockerignore` excludes `.env*`, `bin/obj` and local tooling files.
- Local scan: `docker scout cves` or Trivy before publishing.

## 3. Full compose (Phase 13)

Services: `postgres`, `localstack` (init script creates the queues + DLQ), `aspire-dashboard`, `simulator`, `api`, `worker`, `admin`. Profiles: `deps` (dependencies only, for `dotnet run`) and `full`.

## 4. CI (Phase 14)

`ci.yml` pipeline on PRs and `main`:
1. `actions/checkout`, `setup-dotnet` (10.0.x), NuGet cache.
2. `dotnet restore` (lockfile), `dotnet build -warnaserror`, `dotnet format --verify-no-changes`.
3. `dotnet test` unit + architecture; integration with Testcontainers (Docker available on the ubuntu runner).
4. Security: CodeQL (C#), dependency review, gitleaks, `dotnet list package --vulnerable`.
5. Image build + Trivy; push to ECR only on `main` with OIDC (no access keys in GitHub).
6. Artifacts: test results, coverage report (informational).
Least privilege (explicit `permissions:`), no secrets in logs, `concurrency` to cancel stale runs.

## 5. Terraform (Phase 15)

```
infra/terraform/
├── modules/ (network, ecs-service, rds, sqs, secrets, observability, iam)
├── envs/dev/ (main.tf, variables.tf, outputs.tf, dev.tfvars.example)
└── README.md (how to apply, cost, destroy)
```
- Remote state: S3 + DynamoDB lock (created by a documented manual bootstrap) — or local state at first (dev, one person) with a warning.
- `terraform fmt/validate` in CI; `plan` on PRs (with authorization), manual `apply`.
- Sensitive variables via `TF_VAR_*`/Secrets Manager; real `*.tfvars` ignored by Git.

## 6. Cloud deploy (Phase 16)

1. `terraform apply` (envs/dev).
2. Push the images to ECR (CI or a local `docker push` with temporary SSO credentials).
3. One-off migration task: `aws ecs run-task ... --overrides '{"containerOverrides":[{"name":"api","command":["migrate"]}]}'` (the `migrate` command on the Api host applies `Database.Migrate()` **only** when invoked explicitly).
4. Update the services (`aws ecs update-service --force-new-deployment` or a new task definition via Terraform/CI).
5. Verify: `/health/ready` on the ALB, E2E flow against the cloud (hosted simulator), alarms.
6. At the end of the session: `terraform destroy` (cost). Record the observed cost.

## 7. Rollback
ECS deployment circuit breaker (automatic rollback if the tasks do not become healthy); migrations are additive (expand/contract) so code can be rolled back without reverting the schema.

## 8. Pre-publication checklist (before any push/publication)

- [ ] `git status` and `git ls-files` do not list local tooling files, editor/agent configuration, scratch files or transcripts.
- [ ] `.gitignore` reviewed (`git check-ignore -v <path>` confirms local files are ignored).
- [ ] `gitleaks detect --source .` (or `git secrets`) with no findings; manual search for `password`, `secret`, `key=`, `AKIA`, `BEGIN PRIVATE KEY`, connection strings.
- [ ] No `appsettings*.json` with real values; `.env.example` with placeholders only.
- [ ] No dumps (`*.sql`, `*.dump`), CSVs with data, photos, personal documents.
- [ ] No real personal data (author's or third parties' name/e-mail/phone) in seeds, tests, docs — use fictional data.
- [ ] Clean Git history (if something leaked in an earlier commit: rewrite history **before** the first push, never after).
- [ ] README and docs do not claim a real integration, real production, scale or users.
- [ ] Third-party licences respected (third-party code, if any, with attribution).
