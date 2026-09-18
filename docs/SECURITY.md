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
| Cliente lê/cancela pedido de outro | Elevation/Information disclosure | autorização por recurso no caso de uso (`order.CustomerId == principal.CustomerId`), 404 em vez de 403 para não revelar existência; testes T16 | **4 ✔** (`OrderQueries.Visible()`, `CancelOrderHandler`; `Customer_CannotSeeOrCancel_AnotherCustomersOrder`) |
| Credential stuffing no login | Spoofing/DoS | rate limit por IP+conta, hash PBKDF2 (`PasswordHasher`), mensagens genéricas, lockout progressivo (P2) | **3 ✔** (rate limit por IP, PBKDF2, 401 idêntico + decoy; lockout por conta pendente) |
| Token JWT roubado | Spoofing | expiração curta (15 min), `aud`/`iss` validados, HTTPS obrigatório fora de dev, sem token em logs/URLs; refresh token com rotação (P2) | **3 ✔** (exp 15 min, iss/aud/alg validados, sem token em logs; HTTPS/HSTS na Fase 10) |
| Overposting (`status`, `total`, `customerId` no body) | Tampering | DTOs de request sem esses campos; valores derivados do servidor; testes | **4 ✔** (`PlaceOrderRequest`; campos extras ignorados, teste dedicado) |
| SQL injection | Tampering | EF Core parametrizado; `FromSql` só interpolado; sem concatenação; analyzer EF | 2 |
| Flood de webhooks/pedidos | DoS | rate limiting (`AddRateLimiter`), limite de corpo, timeouts, fila absorve picos | 10 |
| SSRF | Tampering | o sistema **nunca** chama URLs fornecidas por usuários; URLs de providers são configuração validada (allowlist de hosts) | 10 |
| Vazamento de PII em logs | Information disclosure | redação (mascarar e-mail/telefone), nunca logar corpo de webhook/payload de pedido completo, `LoggerMessage` com campos explícitos | 10 |
| Secrets no repositório | Information disclosure | user-secrets/env local, Secrets Manager em nuvem, gitleaks em CI, checklist pré-publicação | 1/14/20 |
| Dependência vulnerável | Supply chain | `dotnet list package --vulnerable`, dependency review, Trivy nas imagens, CPM com versões fixas | 10/14 |
| Migrations destrutivas automáticas | Tampering/Availability | nunca `Migrate()` automático; script idempotente revisado; task one-off | 16 |
| Acesso de rede ao banco/fila | Information disclosure | RDS em subnet privada, SG só do ECS, IAM task role least privilege, sem IP público no banco | 15 |

## 2. Autenticação e autorização — IMPLEMENTADO (Fase 3, 2026-09-18)

| Item | Implementação | Evidência |
|---|---|---|
| Usuários | tabela `users` própria (`User` agregado, roles fixas `Customer/Operator/Admin` em `text[]`); **não** usa o framework ASP.NET Identity, só a classe `PasswordHasher<User>` (ADR-008) | `src/FulfillmentHub.Domain/Identity/User.cs`, `src/FulfillmentHub.Infrastructure/Identity/IdentityPasswordHasher.cs` |
| Hash de senha | `PasswordHasher<TUser>` v3: PBKDF2-HMAC-SHA512, salt por senha, 100k iterações, formato versionado; re-hash transparente no login quando o formato evoluir (`SuccessRehashNeeded`); hash malformado no banco = senha errada, nunca 500 | `IdentityPasswordHasherTests`, `LoginHandler` |
| Emissão de token | `JsonWebTokenHandler` (stack atual do IdentityModel), HS256 com chave ≥ 32 bytes; claims **mínimas**: `sub` (user id), `role[]`, `customer_id` (só clientes), `jti`, `iat/nbf/exp/iss/aud`. Sem e-mail/nome no token | `JwtTokenService`, `JwtTokenServiceTests` |
| Expiração | 15 min (`Jwt:AccessTokenLifetimeMinutes`, faixa 1–60), clock skew 30 s; sem refresh token na v1 (P2 BL-033) | `JwtOptions`, `AuthEndpointsTests.ExpiredToken_Returns401` |
| Validação de token | `AddJwtBearer` com issuer, audience, assinatura (`ValidAlgorithms = [HS256]`), lifetime e `RequireExpirationTime`; `MapInboundClaims = false` (nomes curtos, sem mapeamento mágico) | `JwtBearerOptionsSetup`, testes de token adulterado/expirado/chave estranha |
| Segredo | `Jwt:SigningKey` **nunca** em `appsettings` (valor vazio no arquivo); user-secrets em dev, Secrets Manager na AWS; `ValidateOnStart` recusa subir com chave ausente ou < 32 chars | `AuthorizationTests.Api_RefusesToStart_WhenJwtSigningKeyIsTooShort` |
| Autorização | `FallbackPolicy = RequireAuthenticatedUser` (negar por padrão); policies `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly` (`RequireRole`); `AllowAnonymous` explícito só em `/health/*`, `/auth/login`, OpenAPI/Scalar (Development) | `AuthorizationPolicies`, `Program.cs`, `AuthorizationTests` (200/403/401) |
| Rota inexistente | anônimo → **401** (a fallback policy vale mesmo sem endpoint: não revela rotas); autenticado → 404 ProblemDetails | `RequestPipelineTests` |
| Login | `POST /api/v1/auth/login`: validação nativa do .NET 10 (`AddValidation`, DataAnnotations) → 400 ProblemDetails; falha → **401 idêntico** para e-mail inexistente, senha errada e usuário inativo (`auth.invalid_credentials`), com verificação de hash contra um *decoy* quando não há usuário (mesmo custo → sem oráculo de tempo) | `LoginHandler`, `AuthEndpointsTests.Login_WrongPassword_UnknownEmail_AndInactiveUser_AreIndistinguishable` |
| Rate limiting | `AddRateLimiter`: janela fixa de 5 tentativas/min por IP de origem no login → 429 (`RejectionStatusCode`) | `ApiSecurityServiceCollectionExtensions`, `AuthEndpointsTests.Login_IsRateLimitedPerClient` |
| Logs | eventos `3000/3001` com `UserId` (sucesso) ou motivo interno (`UnknownUser/WrongPassword/InactiveUser/MalformedEmail`) — **sem e-mail, senha ou token**; EF sem `EnableSensitiveDataLogging` (parâmetros não são logados) | verificação manual do log do host em 2026-09-18 |
| Principal na aplicação | `ICurrentUser` (Application) materializado das claims por request (`HttpContextCurrentUser`); casos de uso fazem autorização por recurso a partir dele (Fase 4) | `GET /api/v1/me` |
| Admin | `GET /api/v1/users/{id}` (AdminOnly) expõe e-mail/roles/status de um usuário — único endpoint administrativo desta fase | `UsersEndpoints` |
| Seed | `dotnet run --project src/FulfillmentHub.Api -- seed`: só em Development, senhas vindas de user-secrets (`Seed:*Password`, ≥ 12 chars), dados fictícios, idempotente | `DevelopmentSeeder` |

### Limitações conhecidas (registradas)
- Sem refresh/revogação de token: um token vazado vale até 15 min (BL-033, P2).
- Sem lockout progressivo por conta (só rate limit por IP): atacante distribuído pode tentar 5/min por IP (P2).
- Rate limit por `RemoteIpAddress`: atrás do ALB (Fase 16) exige `ForwardedHeaders` configurado com proxies conhecidos, senão todos compartilham o IP do balanceador (BL-106).
- HS256 compartilha a mesma chave entre emissor e validador (Api/Admin); RS256 só se surgir mais de um emissor (ADR-008).
- Sem MFA, sem OAuth/OIDC para terceiros (fora de escopo, ADR-008).

### Desenho original (mantido para referência)
- **Usuários próprios** (tabela `users`), senha com `PasswordHasher<User>` (PBKDF2-HMAC-SHA512, iterações padrão do ASP.NET Core Identity — só a classe, não o framework). Política: ≥ 12 caracteres (sem regras bobas de composição).
- **JWT bearer** emitido por `POST /auth/login`: HS256 com chave ≥ 256 bits vinda de secrets; claims `sub`, `role[]`, `customer_id` (quando aplicável), `jti`; `exp` 15 min; `iss`/`aud` fixos e validados; clock skew 30 s.
- **Autorização**: `FallbackPolicy` = usuário autenticado (negar por padrão). Policies: `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly`. Autorização **por recurso** dentro do caso de uso (o principal é passado como `ICurrentUser`), com testes.
- Admin UI (Blazor Server): cookie auth com `SameSite=Strict`, antiforgery nativo, mesmas policies (Fase 17).

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
| A01 | Broken Access Control | negar por padrão, policies, autorização por recurso, testes T16, 404 vs 403 | **implementado (Fases 3–4)**: `FallbackPolicy` ✔, policies por papel ✔, autorização por recurso em `OrderQueries`/`CancelOrderHandler` (pedido alheio → 404, sem liberar estoque) com `OrderAccessAndCancelTests` ✔ |
| A02 | Cryptographic Failures | PBKDF2 para senhas, HMAC-SHA256 webhooks, TLS fora de dev, JWT key ≥ 256 bits, sem algoritmos "none" | **parcial (Fase 3)**: PBKDF2-HMAC-SHA512 ✔, chave JWT ≥ 32 bytes validada no start ✔, `ValidAlgorithms=[HS256]` ✔; TLS/HMAC webhooks pendentes |
| A03 | Injection | EF Core parametrizado, validação de entrada, sem SQL dinâmico, sem `Process.Start` | planejado |
| A04 | Insecure Design | threat model, idempotência, limites (itens por pedido, tamanho de corpo), reconciliação | **parcial (Fase 4)**: idempotência real com chave por usuário ✔ (ADR-010), limites 1–50 itens / 1–99 unidades ✔, overposting: DTOs sem `status/total/customerId` e teste `PlaceOrder_IgnoresServerControlledFields` ✔; limite de corpo e reconciliação pendentes |
| A05 | Security Misconfiguration | headers (`X-Content-Type-Options`, `Referrer-Policy`, CSP na Admin), CORS explícito, erros sem stack fora de dev, OpenAPI só em dev, containers não-root | planejado |
| A06 | Vulnerable Components | CPM, `--vulnerable`, dependency review, Trivy, imagens base atualizadas | planejado |
| A07 | Identification & Authentication Failures | rate limit de login, mensagens genéricas, exp curta, sem enumeração de usuários | **implementado (Fase 3)**: 5/min por IP ✔, 401 idêntico + decoy hash ✔, exp 15 min ✔, testes `AuthEndpointsTests` |
| A08 | Software & Data Integrity Failures | assinatura de webhooks, outbox (integridade de eventos), lockfile de pacotes, CI com permissões mínimas | planejado |
| A09 | Security Logging & Monitoring | logs estruturados de auth (sucesso/falha), webhooks rejeitados, alertas de 401/403 anômalos e DLQ, sem PII | **parcial (Fase 3)**: eventos 3000/3001 de login sem PII ✔; alertas na Fase 11/16 |
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
