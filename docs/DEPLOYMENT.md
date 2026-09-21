# DEPLOYMENT — FulfillmentHub

How the system is packaged, published and operated per environment. Phases 13 (Docker), 14 (CI), 15 (Terraform), 16 (cloud).
Written in Phase 0; sections 2–4 record what has been executed since. The code is published at https://github.com/Gabriel-PereiraL/fullfillmentHub (manual push, since 2026-09-18); images (Phase 13) and CI/SAST (Phase 14) are implemented and run on GitHub Actions; ECR push and AWS (Phases 15–16) remain future work.

## 1. Environments

| Environment | Infra | Database | Queue | Secrets | Observability | How it starts |
|---|---|---|---|---|---|---|
| local | docker compose | Postgres 17 container | LocalStack SQS | user-secrets / `.env` | `grafana/otel-lgtm` (Grafana, Prometheus, Tempo, Loki; Aspire Dashboard opt-in, D-78) | `docker compose --profile deps up -d` + `dotnet run`, or everything in containers with `--profile app` (§3) |
| dev (cloud) | ECS Fargate + ALB | RDS PostgreSQL | SQS | Secrets Manager | CloudWatch + X-Ray | Terraform + CI/documented manual deploy |

## 2. Docker images — Implemented (Phase 13, 2026-09-21)

| Image | Dockerfile | Base (runtime) | Size | Notes |
|---|---|---|---|---|
| `fulfillmenthub/api` | `docker/Dockerfile.api` | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` + `icu-libs` | 209 MB | `USER app` (uid 1654), port 8080, `ASPNETCORE_ENVIRONMENT=Production` by default; one-off commands `migrate` and `seed` (the latter Development-only) |
| `fulfillmenthub/worker` | `docker/Dockerfile.worker` | `mcr.microsoft.com/dotnet/runtime:10.0-alpine` + `icu-libs` | 168 MB | no HTTP surface; liveness = `fh.worker.heartbeats` (D-86) |
| `fulfillmenthub/simulator` | `docker/Dockerfile.simulator` | `aspnet:10.0-alpine` + `icu-libs` | 189 MB | development aid; dev-only keys live in its `appsettings.Development.json` |

- Multi-stage: `sdk:10.0-alpine` restores **from the lock files** (`--locked-mode`, BL-169) in a cached layer, then publishes; the runtime stage copies only the publish output. Build context = repository root; `.dockerignore` keeps tests, docs, `.env*`, private tooling and `bin/obj` out.
- No `HEALTHCHECK` baked into the image (the orchestrator probes): compose uses `wget` (BusyBox, present in Alpine) against `/health/live` (simulator) and `/health/ready` (API).
- Scan (Gate 13): `trivy image --scanners vuln --severity HIGH,CRITICAL` → **0 findings** on the three images (Alpine 3.24 packages and the .NET 10.0.12 shared frameworks) on 2026-09-21. Re-run with the same pinned scanner as CI: `docker run --rm -v /var/run/docker.sock:/var/run/docker.sock aquasec/trivy:0.74.0 image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed fulfillmenthub/api:local` (SECURITY.md §6.5).
- Tags: `local` from compose, locally and in CI (the `images` job builds through the same compose file and pushes nothing); commit-SHA tags arrive with the ECR push (Phase 16, BL-170); no `latest` outside development. The Admin image waits for Phase 17.

## 3. Full compose — Implemented (Phase 13, 2026-09-21)

`docker compose --profile deps --profile app up --build -d` brings up **postgres**, **localstack**, **otel-lgtm** (profile
`deps`) and, in order, **migrate** (one-off: `FulfillmentHub.Api.dll migrate`, exits 0), **seed** (one-off, Development
data), **simulator**, **api** (`:5000`), **worker** (profile `app`). Always pass both profiles: the app services depend on
the deps services. Configuration reaches the containers as environment variables (`Section__Key`) from a shared YAML
anchor; **secrets come from `.env`** (`JWT_SIGNING_KEY`, `SEED_*_PASSWORD`, `POSTGRES_PASSWORD` are required; the provider
keys default to the simulator's public dev-only values). The hosts run with the Development environment so the seed,
Scalar and the 10-second metric export are on — a local full stack, not a production configuration.

Gate 13 evidence (2026-09-21): `bash scripts/run-e2e.sh` against the containers → **3/3** E2E flows green in 37 s
(`migrate` applied nothing on an already-migrated volume, `seed` wrote 0 rows, `api` healthy, `worker` provisioned the
queues); images 168–209 MB; Trivy 0 HIGH/CRITICAL. Tear down with `docker compose --profile deps --profile app down`
(`-v` also drops the volumes).

## 4. CI — Implemented (GitHub Actions, 2026-09-21, D-81); ECR push Planned (Phase 16)

Two workflows in `.github/workflows/`, both with explicit least-privilege `permissions:` and `concurrency` cancelling
stale runs; **no repository secrets** are required (the tests generate their own keys and containers). Every action is
pinned to a full commit SHA and the Trivy image to a digest (SECURITY.md §6.5).

| Workflow | Trigger | Jobs / steps | Gate |
|---|---|---|---|
| `ci.yml` — *CI* | push to `main`, pull requests, manual | **build-test** on `ubuntu-latest`: `setup-dotnet` from `global.json` → `dotnet restore` → `dotnet build -c Release` (`TreatWarningsAsErrors`, analyzers) → `dotnet format --verify-no-changes` → `dotnet list package --vulnerable --include-transitive` (fails on any advisory) → `dotnet test --solution` (unit, architecture, integration with Testcontainers: PostgreSQL + LocalStack on the runner's Docker) → JUnit results artifact. **dependency-review** (pull requests only, fails on ≥ moderate). **secrets**: gitleaks over the full history. **images** (Phase 13/14): `docker compose build` of the three images → size gate (< 250 MB) → Trivy `HIGH,CRITICAL --ignore-unfixed --exit-code 1` → full stack up with throwaway secrets written to `.env` on the runner → `FulfillmentHub.E2ETests` against the containers → logs on failure → `down -v` | every step must pass |
| `codeql.yml` — *CodeQL (SAST)* | push to `main`, pull requests, weekly (Mon 06:17 UTC), manual | `codeql-action/init` (C#, `security-extended`, `build-mode: manual`) → `dotnet build -c Release -p:UseSharedCompilation=false` under the tracer → `codeql-action/analyze` → SARIF uploaded to *Security → Code scanning* | `error`-level alerts fail the PR check |

Local equivalents: the same commands in DEVELOPMENT.md §3; `actionlint` was run against both files
(`docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint`, 0 errors).

### 4.1 First execution (2026-09-21) and evidence procedure
Pushed with the owner's authorization on 2026-09-21 (`847bddf..85b7653`). Results of the first runs:

| Workflow | Run | Result |
|---|---|---|
| CodeQL (SAST) | [35605030657](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35605030657) | **green** in 2 min 58 s; 63 C# rules; 1 finding (`cs/log-forging`, medium) triaged as a false positive with justification — SECURITY.md §6.4; 0 open alerts |
| CI | [35605030618](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35605030618) | build, format, vulnerable-package gate and gitleaks **green**; tests **230/231** — `PaymentFlowTests.PlaceOrder_InitiatesPayment_AndWebhookMarksOrderPaid` timed out waiting for `Paid` because the order was already `Delivered` (fast runner; the poller compared statuses for equality). Fixed in the test support (the poller now accepts a later happy-path status); reference run on the fix commit `0b2ecde`: [35606335238](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35606335238) — **green, 231/231** in 2 min 11 s; CodeQL [35606335294](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35606335294) green |

**Phases 11–14 remote gate (2026-09-21, commit `907478d`)** — CI [35623513605](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35623513605): locked-mode restore ✓, build 0 warnings ✓, 248 tests (245 passed, 3 E2E skipped in the unit/integration job) ✓, 0 vulnerable packages ✓, gitleaks ✓; **images job** 3 min 33 s: images built on the runner at 144/117/130 MB (api/worker/simulator) ✓, Trivy 0 HIGH/CRITICAL on all three ✓, stack ready after 4 s ✓, **E2E 3/3 against the containers** ✓, `down -v` removed containers, network and volumes ✓. CodeQL [35623513653](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35623513653) green (63 rules, the single already-triaged finding, 0 open alerts).

Procedure for every later run:
1. *Actions* tab → workflow *CI* → the run for the pushed commit must be green; download the `test-results` artifact.
2. *Actions* tab → *CodeQL (SAST)* → green run; *Security → Code scanning* lists the analysis (tool "CodeQL",
   language C#) and any alerts. Record the run URL and the alert count in `docs/SECURITY.md` §6 and in the README.
3. If *Security → Code scanning* shows "default setup" enabled, disable it (Settings → Code security) — default setup and
   the advanced workflow cannot coexist for the same language.
4. Recommended repository settings (owner action, not code): enable *Secret scanning* and *Push protection*; enable
   the *Dependency graph* (required by dependency review; on for public repositories by default).
5. Triage any CodeQL finding with the procedure in `docs/SECURITY.md` §6.2 before adding the badge to the README.

### 4.2 Not yet in CI
Push to ECR with OIDC (Phase 16, BL-170: needs the AWS account) and a coverage report (informational, P2). Restore runs
in locked mode since 2026-09-21 (BL-169). The `images` job ran green on GitHub on 2026-09-21 (run 35623513605).

Not yet *exercised*: the `dependency-review` job is wired but only runs on pull requests, and every change so far
reached `main` by direct push, so it has never executed (`skipped` in every run). The first pull request will be its
first run.

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
