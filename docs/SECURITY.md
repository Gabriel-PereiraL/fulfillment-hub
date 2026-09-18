# SECURITY — FulfillmentHub

Segurança é requisito de primeira classe. Este documento é o plano e, a partir da Fase 10, o **registro de evidências**
(cada item aponta para código/teste). Nada aqui deve afirmar "implementado" antes de existir.

## 1. Threat model simplificado

### Ativos
Dados de clientes (nome, e-mail, telefone, endereço), pedidos e valores, credenciais de usuários (hash), segredos de
integração (chaves HMAC, credenciais do "provider", JWT signing key, connection string), integridade do estado dos
pedidos/pagamentos/entregas, disponibilidade da API e do worker.

### Superfície de ataque
| Entrada | Quem chega | Risco principal |
|---|---|---|
| API pública (`/orders`, `/auth`) | clientes autenticados / anônimos (login) | credential stuffing, IDOR/broken access control, abuso (rate), injeção, overposting |
| Webhooks (`/webhooks/*`) | "providers" (qualquer um que conheça a URL) | forjar eventos (pagamento "pago"), replay, flood |
| Admin UI | operadores/admins | escalada de privilégio, CSRF (Blazor server), exposição de dados |
| Banco/fila/segredos | infraestrutura | credenciais vazadas, acesso de rede indevido |
| Cadeia de dependências | NuGet, imagens base | vulnerabilidades conhecidas |
| Repositório | público no futuro | vazamento de secrets/arquivos privados no histórico |

### Atores
Usuário malicioso autenticado (cliente), atacante anônimo na internet, insider com acesso ao repositório/CI, dependência comprometida.

### Ameaças priorizadas (STRIDE resumido)
| Ameaça | Categoria | Mitigação planejada | Evidência (fase) |
|---|---|---|---|
| Webhook forjado confirma pagamento | Spoofing/Tampering | HMAC-SHA256 com chave por provider, comparação em tempo constante, tolerância de timestamp (5 min), dedup por event id, **e** estado verificado por reconciliação (`GET` no provider) antes de ações irreversíveis de alto valor (decisão: reconciliar sempre que webhook mudar para `paid`? — ver D-P5) | 5/7/10 |
| Cliente lê/cancela pedido de outro | Elevation/Information disclosure | autorização por recurso no caso de uso (`order.CustomerId == principal.CustomerId`), 404 em vez de 403 para não revelar existência; testes T16 | 4/10 |
| Credential stuffing no login | Spoofing/DoS | rate limit por IP+conta, hash PBKDF2 (`PasswordHasher`), mensagens genéricas, lockout progressivo (P2) | 3/10 |
| Token JWT roubado | Spoofing | expiração curta (15 min), `aud`/`iss` validados, HTTPS obrigatório fora de dev, sem token em logs/URLs; refresh token com rotação (P2) | 3 |
| Overposting (`status`, `total`, `customerId` no body) | Tampering | DTOs de request sem esses campos; valores derivados do servidor; testes | 4 |
| SQL injection | Tampering | EF Core parametrizado; `FromSql` só interpolado; sem concatenação; analyzer EF | 2 |
| Flood de webhooks/pedidos | DoS | rate limiting (`AddRateLimiter`), limite de corpo, timeouts, fila absorve picos | 10 |
| SSRF | Tampering | o sistema **nunca** chama URLs fornecidas por usuários; URLs de providers são configuração validada (allowlist de hosts) | 10 |
| Vazamento de PII em logs | Information disclosure | redação (mascarar e-mail/telefone), nunca logar corpo de webhook/payload de pedido completo, `LoggerMessage` com campos explícitos | 10 |
| Secrets no repositório | Information disclosure | user-secrets/env local, Secrets Manager em nuvem, gitleaks em CI, checklist pré-publicação | 1/14/20 |
| Dependência vulnerável | Supply chain | `dotnet list package --vulnerable`, dependency review, Trivy nas imagens, CPM com versões fixas | 10/14 |
| Migrations destrutivas automáticas | Tampering/Availability | nunca `Migrate()` automático; script idempotente revisado; task one-off | 16 |
| Acesso de rede ao banco/fila | Information disclosure | RDS em subnet privada, SG só do ECS, IAM task role least privilege, sem IP público no banco | 15 |

## 2. Autenticação e autorização (desenho)

- **Usuários próprios** (tabela `users`), senha com `PasswordHasher<User>` (PBKDF2-HMAC-SHA512, iterações padrão do ASP.NET Core Identity — só a classe, não o framework). Política: ≥ 12 caracteres (sem regras bobas de composição).
- **JWT bearer** emitido por `POST /auth/login`: HS256 com chave ≥ 256 bits vinda de secrets (dev: user-secrets; AWS: Secrets Manager); claims `sub`, `email`, `role[]`, `customer_id` (quando aplicável), `jti`; `exp` 15 min; `iss`/`aud` fixos e validados; clock skew 30 s.
- Por que não OAuth/OIDC externo: fora do escopo (o objetivo é demonstrar autenticação/autorização no ASP.NET Core, não delegar). Registrado como possível extensão.
- **Autorização**: `FallbackPolicy` = usuário autenticado (negar por padrão). Policies: `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly`. Autorização **por recurso** dentro do caso de uso (o principal é passado como `ICurrentUser`), com testes.
- Endpoints sem JWT: `/health/*` (sem detalhes sensíveis em `ready`), `/webhooks/*` (assinatura HMAC), `/auth/login`, OpenAPI só em Development.
- Admin UI (Blazor Server): cookie auth com `SameSite=Strict`, antiforgery nativo, mesmas policies.

## 3. Segredos e configuração

| Ambiente | Mecanismo | Regras |
|---|---|---|
| Local | `dotnet user-secrets` (Api, Worker, Admin) e/ou `.env` (compose) ignorado; `.env.example` sem valores reais | `appsettings*.json` nunca contêm secrets; chaves do simulator são valores de desenvolvimento óbvios (`dev-only-...`) |
| Testes | valores gerados por teste / Testcontainers | |
| AWS | **Secrets Manager** (connection string, JWT key, HMAC keys) injetado na task definition via `secrets` (não `environment`); Parameter Store para config não sensível | task role com `secretsmanager:GetSecretValue` restrito ao ARN; rotação manual documentada |

Proibido em qualquer lugar: secrets em código, commits, logs, URLs, mensagens de erro, OpenAPI.

## 4. OWASP Top 10 (2021) — checklist (preencher com evidências na Fase 10)

| # | Categoria | Aplicação no projeto | Status |
|---|---|---|---|
| A01 | Broken Access Control | negar por padrão, policies, autorização por recurso, testes T16, 404 vs 403 | planejado |
| A02 | Cryptographic Failures | PBKDF2 para senhas, HMAC-SHA256 webhooks, TLS fora de dev, JWT key ≥ 256 bits, sem algoritmos "none" | planejado |
| A03 | Injection | EF Core parametrizado, validação de entrada, sem SQL dinâmico, sem `Process.Start` | planejado |
| A04 | Insecure Design | threat model, idempotência, limites (itens por pedido, tamanho de corpo), reconciliação | planejado |
| A05 | Security Misconfiguration | headers (`X-Content-Type-Options`, `Referrer-Policy`, CSP na Admin), CORS explícito, erros sem stack fora de dev, OpenAPI só em dev, containers não-root | planejado |
| A06 | Vulnerable Components | CPM, `--vulnerable`, dependency review, Trivy, imagens base atualizadas | planejado |
| A07 | Identification & Authentication Failures | rate limit de login, mensagens genéricas, exp curta, sem enumeração de usuários | planejado |
| A08 | Software & Data Integrity Failures | assinatura de webhooks, outbox (integridade de eventos), lockfile de pacotes, CI com permissões mínimas | planejado |
| A09 | Security Logging & Monitoring | logs estruturados de auth (sucesso/falha), webhooks rejeitados, alertas de 401/403 anômalos e DLQ, sem PII | planejado |
| A10 | SSRF | nenhuma URL de usuário é chamada; hosts de providers em allowlist de configuração | planejado |

## 5. Dados pessoais (LGPD — princípio de minimização)
- Coletar só o necessário (nome, e-mail, telefone, endereço de entrega). Telefone/e-mail mascarados em logs e na Admin (exibição completa só para Admin com motivo).
- Dados enviados ao "provider" limitados ao necessário para a entrega (simulado).
- Retenção: `webhook_events` e `outbox_messages` processados podem ser expurgados após 30 dias (job P2). `idempotency_records` expiram em 24 h.
- Sem dados reais: seeds usam dados fictícios.

## 6. SAST / dependency scanning (Fase 10/14)
- Local: analyzers de segurança do .NET (`CA3xxx`, `CA5xxx` em `AnalysisLevel latest-recommended`), `dotnet list package --vulnerable --include-transitive`.
- CI (após autorização de GitHub): CodeQL (C#), GitHub dependency review, gitleaks (secrets), Trivy (imagens), `dotnet format --verify-no-changes`.

## 7. Checklist pré-publicação (usar na Fase 20 e antes de qualquer push)
Ver `DEPLOYMENT.md` §"Checklist anti-vazamento".

## 8. Decisões pendentes de segurança
- **D-P5**: reconciliar com `GET` no provider antes de aplicar webhook `paid` (custo: +1 chamada; ganho: webhook forjado não confirma pagamento mesmo com chave vazada). Proposta: sim para `paid` e `refunded`; não para eventos de entrega.
- Lockout progressivo de login (P2). Refresh token com rotação (P2). Chave HMAC por webhook com rotação (P2).
