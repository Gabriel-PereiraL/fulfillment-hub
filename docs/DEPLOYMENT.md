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

## 4. CI — Implemented (GitHub Actions, 2026-09-21, D-81); image pipeline Planned (Phase 14)

Two workflows in `.github/workflows/`, both with explicit least-privilege `permissions:` and `concurrency` cancelling
stale runs; **no repository secrets** are required (the tests generate their own keys and containers).

| Workflow | Trigger | Jobs / steps | Gate |
|---|---|---|---|
| `ci.yml` — *CI* | push to `main`, pull requests, manual | **build-test** on `ubuntu-latest`: `setup-dotnet` from `global.json` → `dotnet restore` → `dotnet build -c Release` (`TreatWarningsAsErrors`, analyzers) → `dotnet format --verify-no-changes` → `dotnet list package --vulnerable --include-transitive` (fails on any advisory) → `dotnet test --solution` (unit, architecture, integration with Testcontainers: PostgreSQL + LocalStack on the runner's Docker) → JUnit results artifact. **dependency-review** (pull requests only, fails on ≥ moderate). **secrets**: gitleaks over the full history | every step must pass |
| `codeql.yml` — *CodeQL (SAST)* | push to `main`, pull requests, weekly (Mon 06:17 UTC), manual | `codeql-action/init` (C#, `security-extended`, `build-mode: manual`) → `dotnet build -c Release -p:UseSharedCompilation=false` under the tracer → `codeql-action/analyze` → SARIF uploaded to *Security → Code scanning* | `error`-level alerts fail the PR check |

Local equivalents: the same commands in DEVELOPMENT.md §3; `actionlint` was run against both files
(`docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint`, 0 errors).

### 4.1 Evidence procedure (needs the repository owner to push)
The workflows were written and linted locally; **they have not run on GitHub yet** — running them requires a push,
which is never done without the owner's explicit authorization (repository rule). After the push:
1. *Actions* tab → workflow *CI* → the run for the pushed commit must be green; download the `test-results` artifact.
2. *Actions* tab → *CodeQL (SAST)* → green run; *Security → Code scanning* lists the analysis (tool "CodeQL",
   language C#) and any alerts. Record the run URL and the alert count in `docs/SECURITY.md` §6 and in the README.
3. If *Security → Code scanning* shows "default setup" enabled, disable it (Settings → Code security) — default setup and
   the advanced workflow cannot coexist for the same language.
4. Recommended repository settings (owner action, not code): enable *Secret scanning* and *Push protection*; enable
   the *Dependency graph* (required by dependency review; on for public repositories by default).
5. Triage any CodeQL finding with the procedure in `docs/SECURITY.md` §6.2 before adding the badge to the README.

### 4.2 Not yet in CI (Phase 13/14)
Image build (`docker build` of Api/Worker/Simulator), Trivy scan, push to ECR with OIDC — they need the Dockerfiles
from Phase 13. Coverage report (informational) and a NuGet lock file (`--locked-mode`, BL-169) are P2.

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
