# DEPLOYMENT — FulfillmentHub

Como o sistema é empacotado, publicado e operado por ambiente. Fases 13 (Docker), 14 (CI), 15 (Terraform), 16 (cloud).
Nesta Fase 0 nada é executado externamente: **sem GitHub, sem AWS, sem push.**

## 1. Ambientes

| Ambiente | Infra | Banco | Fila | Segredos | Observabilidade | Como sobe |
|---|---|---|---|---|---|---|
| local | docker compose | Postgres 17 container | LocalStack SQS (Fase 9) | user-secrets / `.env` | Aspire Dashboard | `docker compose up -d` + `dotnet run` (ou tudo em containers na Fase 13) |
| dev (cloud) | ECS Fargate + ALB | RDS PostgreSQL | SQS | Secrets Manager | CloudWatch + X-Ray | Terraform + CI/deploy manual documentado |

## 2. Imagens Docker (Fase 13)

- Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` (build/test/publish) → `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime); `USER app` (não-root, porta 8080); `HEALTHCHECK` chamando `/health/live`.
- Uma imagem por host: `fh-api`, `fh-worker`, `fh-simulator`, `fh-admin`. Tag = SHA curto do commit + `latest` só em dev.
- Sem secrets em build args/layers; `.dockerignore` exclui `.ai/`, `.claude/`, `CLAUDE.md`, `.env*`, `bin/obj`.
- Scan local: `docker scout cves` ou Trivy antes de publicar.

## 3. Compose completo (Fase 13)

Serviços: `postgres`, `localstack` (init script cria filas + DLQ), `aspire-dashboard`, `simulator`, `api`, `worker`, `admin`. Perfis: `deps` (só dependências, para `dotnet run`) e `full`.

## 4. CI (Fase 14 — só após autorização para GitHub)

Pipeline `ci.yml` em PR e `main`:
1. `actions/checkout`, `setup-dotnet` (10.0.x), cache NuGet.
2. `dotnet restore` (lockfile), `dotnet build -warnaserror`, `dotnet format --verify-no-changes`.
3. `dotnet test` unit + architecture; integration com Testcontainers (Docker disponível no runner ubuntu).
4. Segurança: CodeQL (C#), dependency review, gitleaks, `dotnet list package --vulnerable`.
5. Build de imagens + Trivy; push para ECR só em `main` com OIDC (sem access keys no GitHub).
6. Artefatos: resultados de teste, relatório de cobertura (informativo).
Permissões mínimas (`permissions:` explícito), sem secrets em logs, `concurrency` para cancelar runs obsoletos.

## 5. Terraform (Fase 15)

```
infra/terraform/
├── modules/ (network, ecs-service, rds, sqs, secrets, observability, iam)
├── envs/dev/ (main.tf, variables.tf, outputs.tf, dev.tfvars.example)
└── README.md (como aplicar, custo, destroy)
```
- Estado remoto: S3 + DynamoDB lock (criados por bootstrap manual documentado) — ou estado local no início (dev, 1 pessoa) com aviso.
- `terraform fmt/validate` no CI; `plan` em PR (com autorização), `apply` manual.
- Variáveis sensíveis via `TF_VAR_*`/Secrets Manager; `*.tfvars` reais ignorados pelo Git.

## 6. Deploy na nuvem (Fase 16)

1. `terraform apply` (envs/dev).
2. Push das imagens para ECR (CI ou `docker push` local com credenciais temporárias via SSO).
3. Task one-off de migração: `aws ecs run-task ... --overrides '{"containerOverrides":[{"name":"api","command":["migrate"]}]}'` (comando `migrate` no host Api aplica `Database.Migrate()` **apenas** quando invocado explicitamente).
4. Atualizar serviços (`aws ecs update-service --force-new-deployment` ou nova task definition via Terraform/CI).
5. Verificar: `/health/ready` no ALB, fluxo E2E contra a nuvem (simulator hospedado), alarmes.
6. Ao terminar a sessão: `terraform destroy` (custo). Registrar custo observado.

## 7. Rollback
ECS deployment circuit breaker (rollback automático se as tasks não ficam saudáveis); migrations são aditivas (expand/contract) para permitir rollback de código sem reverter schema.

## 8. Checklist anti-vazamento (antes de QUALQUER push/publicação)

- [ ] `git status` e `git ls-files` não listam: `CLAUDE.md`, `.ai/`, `.claude/`, `.skills/`, `*.prompt.md`, transcripts, scratchpads, skills copiadas.
- [ ] `.gitignore` revisado; `git check-ignore -v CLAUDE.md .ai .claude` confirma ignorados.
- [ ] `gitleaks detect --source .` (ou `git secrets`) sem achados; busca manual por `password`, `secret`, `key=`, `AKIA`, `BEGIN PRIVATE KEY`, connection strings.
- [ ] Nenhum `appsettings*.json` com valores reais; `.env.example` só com placeholders.
- [ ] Nenhum dump (`*.sql`, `*.dump`), CSV com dados, foto, documento pessoal.
- [ ] Nenhum dado pessoal real (nome/e-mail/telefone do autor ou de terceiros) em seeds, testes, docs — usar fictícios.
- [ ] Histórico do Git limpo (se algo vazou em commit anterior: reescrever histórico **antes** do primeiro push, nunca depois).
- [ ] README e docs não afirmam integração real, produção real, escala ou usuários.
- [ ] Licenças de terceiros respeitadas (skills copiadas não estão no repo; código de terceiros, se houver, com atribuição).
- [ ] Usuário autorizou explicitamente a criação do remote/push nesta sessão.
