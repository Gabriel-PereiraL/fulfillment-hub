# SECURITY — FulfillmentHub

Security is a first-class requirement. This document is the **evidence record**: every control points to code and
tests, and every item carries one of four statuses — **Implemented** (exists, with evidence), **Partial** (part of the
control exists; the missing part is named), **Planned** (phase given), **Accepted risk** (consciously not mitigated,
with the reason). Nothing here claims "implemented" before it exists.

Last full review: 2026-09-21 (hardening track, D-77/D-87 — Phase 10 closed); consistency pass on the same day after
Phases 11–14 closed (Trivy, containers and lock files moved from `Planned` to `Implemented`; §6.5 added).

## 1. Threat model

### 1.1 Assets

| Asset | Why it matters | Where it lives |
|---|---|---|
| Customer personal data (name, e-mail, phone, delivery address) | LGPD; reputational damage | `customers`, `orders` (address snapshot), simulator delivery requests |
| Orders, amounts and their state | money and stock decisions depend on them | `orders`, `order_items`, `stock` |
| Credentials: password hashes, JWT signing key | account takeover; forging any identity | `users.password_hash`; `Jwt:SigningKey` (user-secrets / Secrets Manager) |
| Integration secrets: provider API key, client credentials, webhook HMAC keys | forging a "paid" payment or a delivery event; calling the provider on our behalf | `Providers:*` (user-secrets / Secrets Manager) |
| Database connection string, queue credentials | full data access | `Database:ConnectionString`, `Messaging:Sqs:*` |
| Integrity of the outbox / inbox / processed-message tables | at-least-once delivery, no lost or duplicated effects | `outbox_messages`, `webhook_events`, `processed_messages`, `idempotency_records` |
| Availability of the API and the Worker | orders cannot be placed; paid orders are not delivered | Api, Worker hosts |
| The public repository and CI | supply chain; leaked secrets in history or workflows | GitHub |

### 1.2 Actors

| Actor | Capability | Motivation |
|---|---|---|
| Anonymous internet client | reaches `/api/v1/auth/login`, `/api/v1/webhooks/*`, `/health/*`, unknown routes | credential stuffing, forged webhooks, DoS, reconnaissance |
| Authenticated customer | valid JWT with `Customer` role | read/cancel other customers' orders, abuse stock or quotes, over-posting |
| Operator / Admin | valid JWT with `Operator`/`Admin` | mistakes; privilege misuse (out of scope: malicious insider) |
| Provider (payment / delivery) | signs webhooks with the shared key | none — but *anyone who knows the URL* can send a webhook, and a leaked key lets them forge one |
| Repository/CI contributor | opens PRs, runs workflows | accidental secret leak; malicious dependency |
| Compromised dependency / base image | runs inside the process | supply chain |

### 1.3 Trust boundaries

```
 Internet ──(1)──► ASP.NET Core API (Kestrel) ──(2)──► PostgreSQL
                       │        ▲                 └──(2)──► SQS (LocalStack locally)
                       │        │ (3) signed webhooks
                       ▼        │
                 (4) Provider simulator (payment / delivery) ◄──(4)── Worker ──(2)──► PostgreSQL / SQS
```

| # | Boundary | What crosses it | Controls at the boundary |
|---|---|---|---|
| 1 | Internet → API | JSON requests, bearer tokens, `X-Correlation-Id`, `Idempotency-Key` | TLS at the edge (Phase 16); JWT validation; deny-by-default authorization; rate limits (login / webhooks / orders); body limits (Kestrel 256 KB, webhooks 64 KB); native validation; security headers; ProblemDetails without internals |
| 2 | Application → data plane | SQL (EF Core), SQS messages | parameterised queries only; secrets from configuration providers, never code; queue messages carry ids/pointers, not payloads (D-73) |
| 3 | Provider → API (webhooks) | signed events | HMAC-SHA256 over the raw body, constant-time compare, 5-min timestamp window, inbox dedup, `paid`/`refunded` re-verified with a `GET` (D-42) |
| 4 | API/Worker → provider | outbound HTTPS with API key / OAuth client credentials | base URLs are validated configuration (no user-supplied URL — SSRF by design); timeouts, retry with backoff, circuit breaker; the token cache never logs the token |

### 1.4 Threats and controls (STRIDE)

| # | Threat | STRIDE | Controls | Status | Evidence |
|---|---|---|---|---|---|
| T1 | Forged webhook confirms a payment | S/T | HMAC key per provider, constant-time compare, timestamp window, inbox `UNIQUE(provider, provider_event_id)`, `paid`/`refunded` confirmed with a `GET` before being applied | **Implemented** | `WebhookSignatureVerifier`, `ApplyPaymentWebhookHandler`; tests `Webhook_WithBadSignature_IsRejected_AndNothingIsPersisted`, `Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted`, `DuplicateWebhook_IsAcknowledged_ButAppliedOnce` |
| T2 | Customer reads / cancels another customer's order | E/I | resource authorization inside the use case; 404 instead of 403 so existence is not revealed | **Implemented** | `OrderQueries.Visible()`, `CancelOrderHandler`; `OrderAccessAndCancelTests.Customer_CannotSeeOrCancel_AnotherCustomersOrder` |
| T3 | Credential stuffing on login | S/D | 5 attempts/min per IP → 429; PBKDF2-HMAC-SHA512 (100k it.); identical 401 + decoy hash for unknown users | **Implemented** (per-IP) · **Accepted risk**: no per-account lockout (see §1.6 R2) | `ApiSecurityServiceCollectionExtensions`, `LoginHandler`; `AuthEndpointsTests.Login_IsRateLimitedPerClient`, `…AreIndistinguishable` |
| T4 | Stolen JWT | S | 15-min expiry, `iss`/`aud`/`alg` validated, no token in logs/URLs, `Cache-Control: no-store` on API responses | **Implemented** · **Accepted risk**: no revocation inside the 15 min (R1) | `JwtTokenService`, `JwtBearerOptionsSetup`; `AuthEndpointsTests.ExpiredToken_Returns401`, tampered/foreign-key tests; `SecurityHeadersTests` |
| T5 | Over-posting (`status`, `total`, `customerId` in the body) | T | request DTOs without server-controlled fields | **Implemented** | `PlaceOrderRequest`; `PlaceOrderTests.PlaceOrder_IgnoresServerControlledFields` |
| T6 | SQL injection | T | EF Core parameterised queries; the only raw SQL is `FromSqlInterpolated` (parameterised); no `*Raw(` API in the code base; EF analyzers under `TreatWarningsAsErrors` | **Implemented** | `grep -rn "FromSqlRaw\|ExecuteSqlRaw" src` → 0; `OutboxProcessor` uses `FromSqlInterpolated`; CodeQL `cs/sql-injection` query in CI (§6) |
| T7 | Flood: login / webhooks / order creation | D | fixed window per IP (login 5/min, webhooks 1200/min), sliding window per user for `POST /orders` (60/min), 64 KB webhook body, 256 KB Kestrel body limit, provider timeouts | **Implemented** | D-69, D-83, D-84; `AuthEndpointsTests.Login_IsRateLimitedPerClient`, `PlaceOrderRateLimitTests`, `WebhookReceiver` (413); Kestrel limit: smoke evidence §1.7 |
| T8 | SSRF | T | the system never calls a user-supplied URL; provider base URLs are validated `Options` (`ValidateOnStart`); the simulator's webhook target is simulator configuration | **Implemented (by design)** | `PaymentProviderOptions`, `DeliveryProviderOptions`; no endpoint accepts a URL (OpenAPI document) |
| T9 | PII or secrets in logs | I | `LoggerMessage` catalog uses ids and enum reasons only; webhook body/signature/key never logged; EF sensitive data logging off; guard test scans every `[LoggerMessage]` for e-mail/phone/password/token/key/body placeholders | **Implemented** | `LogMessagePrivacyTests.LoggerMessages_DoNotCarryPersonalDataOrSecrets` (architecture tests); D-85 |
| T10 | Secrets in the repository / CI | I | empty values in `appsettings*`, user-secrets locally, `.env` ignored, `ValidateOnStart` refuses empty keys; gitleaks in CI; history audited on 2026-09-18 (nothing real) | **Implemented** (gitleaks ran green on 2026-09-21) | `.gitignore`, `JwtOptions`/provider options; `.github/workflows/ci.yml` job `secrets` |
| T11 | Vulnerable dependency | supply chain | CPM with pinned versions; `dotnet list package --vulnerable --include-transitive` as a CI gate; GitHub dependency review on PRs; CodeQL | **Implemented** (package gate green on 2026-09-21; Trivy 0 HIGH/CRITICAL on the three images in CI run 35623513605) | `Directory.Packages.props`; `ci.yml` steps `Vulnerable packages` and `Trivy` (job `images`); `codeql.yml`; DEPLOYMENT.md §2 |
| T12 | Response-level attacks (MIME sniffing, clickjacking of an HTML error page, referrer leaks, cached tokens) | I/T | `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, CSP `default-src 'none'`, `Cache-Control: no-store` on `/api`, HSTS outside Development | **Implemented** · HSTS effective only once TLS/ForwardedHeaders exist (BL-113) | `SecurityHeadersMiddleware`; `SecurityHeadersTests` (4 tests) |
| T13 | Cross-origin browser abuse (CSRF-like calls from another origin) | S | **no CORS policy is registered**, so the API never emits `Access-Control-Allow-*` headers. CORS only governs what a *browser* lets a page on another origin do: read a response, or send a request with a custom header such as `Authorization` (which needs a preflight the API will not approve). It does not stop the request from being sent, and non-browser clients ignore it entirely. Authentication is a bearer token the client itself must place in `Authorization` — browsers never attach it automatically the way they attach cookies — so a page on a foreign origin cannot make an authenticated call without already holding the token, and even then the preflight fails. Classic CSRF therefore has no vector; the residual risk is the token itself (T4/R1). Not enabling CORS was a decision on the real exposure (no browser client on another origin), not a checklist default | **Implemented (by absence — D-82)** | `Program.cs` has no `AddCors`/`UseCors`; if a browser client on another origin appears (the Phase 17 Admin is server-rendered), enable it with an explicit origin allow-list, never `AllowAnyOrigin` |
| T14 | Destructive automatic migrations | T/A | never `Database.Migrate()` at startup; migrations applied by an explicit command / one-off task | **Implemented** (local) · production procedure **Planned** (Phase 16) | `Program.cs` (no `Migrate()`); `DEVELOPMENT.md` §2 |
| T15 | Network exposure of database / queue | I | private subnets, security groups, least-privilege IAM | **Planned** (Phase 15) | `AWS_ARCHITECTURE.md` |
| T16 | Forged delivery event (leaked delivery key) | S/T | signature + aggregate ordering rules (never regresses status) + periodic reconciliation; **not** re-verified with a `GET` | **Accepted risk** (D-59): no money involved, worst case an early status advance corrected by reconciliation | `Delivery.ApplyProviderEvent`, `ReconcileDeliveriesHandler` |

### 1.5 Disposition of items previously marked "planned" (2026-09-21, D-87)

| Item | Decision | Rationale |
|---|---|---|
| Security headers | **Implement** → T12 | cheap, testable, applies to every response |
| Explicit CORS | **Accept as-is (not enabled)** → T13 | no cross-origin browser client; enabling it would widen the surface |
| HTTPS redirection / HSTS | **HSTS: implement** (registered outside Development) · **redirection: defer to Phase 16** (BL-113) | TLS terminates at the ALB; Kestrel has no certificate in containers |
| `ForwardedHeaders` | **Defer to Phase 16** (BL-113) | only meaningful behind the load balancer; must be configured with known proxies |
| Body size limit (general) | **Implement** → T7 (Kestrel 256 KB, D-83) | bounded cost per request; webhooks already limited in code |
| `POST /orders` rate limit | **Implement** → T7 (per user, D-84) | one account must not exhaust stock reservations/quotes |
| PII redaction in logs | **Implement as guard test** → T9 (D-85) | nothing to redact if the field never reaches the logger |
| SSRF | **Document (by design)** → T8 | no user-supplied URL exists |
| SAST / CodeQL, dependency review, gitleaks | **Implement** → §6 | real SAST, automatic in CI |
| Trivy on images | **Defer to Phase 13/14** → done the same day (T11): scanned locally at Gate 13 and in every CI run since | there were no images yet when the track started |
| Progressive per-account lockout | **Accept risk** (R2) | distributed 5/min/IP is bounded; lockout adds a DoS vector against legitimate users |
| Refresh / revocation of tokens | **Accept risk** (R1) | 15-min window; revocation list only if a real need appears (BL-033, P2) |
| HMAC key rotation per provider | **Accept risk** (R3) | single key per provider, rotation is a manual redeploy today |
| Secrets Manager, RDS in private subnet, IAM | **Defer to Phases 15–16** | AWS is out of this track by invariant |

### 1.6 Residual risks (accepted, on record)

| # | Risk | Why accepted | Trigger to revisit |
|---|---|---|---|
| R1 | A leaked access token is valid for up to 15 minutes; there is no revocation | short lifetime; no refresh tokens means no long-lived credential to steal | a real client that needs sessions longer than 15 min (Admin UI, Phase 17) |
| R2 | Login is rate-limited per IP, not per account; a distributed attacker gets 5/min per IP | PBKDF2 cost + rate limit make the attack slow; per-account lockout would let an attacker lock legitimate users out | evidence of credential stuffing in the login-failure metrics/alerts |
| R3 | One HMAC key per provider, rotated only by redeploy | simulated providers; rotation without downtime needs dual-key acceptance | a real provider contract |
| R4 | Delivery webhooks are not re-verified with the provider (T16) | no financial effect; reconciliation corrects | provider events start carrying financial data |
| R5 | Rate limiting keys on `RemoteIpAddress`; behind the ALB every client shares one IP until `ForwardedHeaders` is configured | no load balancer exists yet | Phase 16 deployment (BL-113 is P0 there) |
| R6 | The Kestrel body limit is not covered by an automated test (the in-memory test server bypasses Kestrel) | verified manually (§1.7); the webhook limit *is* tested | the black-box E2E suite (Phase 12) runs against real Kestrel hosts but does not yet include a 413 case — adding one closes R6 |
| R7 | CodeQL findings are triaged by one person; no second reviewer | solo project | a second maintainer |

### 1.7 Known limitations and manual evidence

- **Kestrel 256 KB limit** — smoke on 2026-09-21 against `dotnet run --project src/FulfillmentHub.Api` (see `docs/incidents/2026-09-21-slow-provider-drill.md`, appendix): `POST /api/v1/orders` with a 300 KB JSON body → `413 Payload Too Large`; 200 KB → normal validation response.
- No MFA, no OAuth/OIDC for third parties (out of scope, ADR-008).
- HS256 shares the key between issuer and validator (single issuer, ADR-008).
- Unknown routes answer 401 to anonymous callers (routes are not revealed) — a deliberate choice that also hides `/health` from *unauthenticated* discovery only by convention: health endpoints are explicitly anonymous.

## 2. Authentication and authorization — Implemented (Phase 3, 2026-09-18)

| Item | Implementation | Evidence |
|---|---|---|
| Users | own `users` table (`User` aggregate, fixed roles `Customer/Operator/Admin` in `text[]`); the ASP.NET Identity framework is **not** used, only the `PasswordHasher<User>` class (ADR-008) | `src/FulfillmentHub.Domain/Identity/User.cs`, `src/FulfillmentHub.Infrastructure/Identity/IdentityPasswordHasher.cs` |
| Password hashing | `PasswordHasher<TUser>` v3: PBKDF2-HMAC-SHA512, per-password salt, 100k iterations, versioned format; transparent re-hash at login when the format evolves (`SuccessRehashNeeded`); a malformed hash in the database means "wrong password", never a 500 | `IdentityPasswordHasherTests`, `LoginHandler` |
| Token issuance | `JsonWebTokenHandler` (the current IdentityModel stack), HS256 with a key ≥ 32 bytes; **minimal** claims: `sub` (user id), `role[]`, `customer_id` (customers only), `jti`, `iat/nbf/exp/iss/aud`. No e-mail or name in the token | `JwtTokenService`, `JwtTokenServiceTests` |
| Expiry | 15 min (`Jwt:AccessTokenLifetimeMinutes`, range 1–60), 30 s clock skew; no refresh token in v1 (P2, BL-033) | `JwtOptions`, `AuthEndpointsTests.ExpiredToken_Returns401` |
| Token validation | `AddJwtBearer` with issuer, audience, signature (`ValidAlgorithms = [HS256]`), lifetime and `RequireExpirationTime`; `MapInboundClaims = false` (short names, no magic mapping) | `JwtBearerOptionsSetup`, tampered/expired/foreign-key token tests |
| Secret | `Jwt:SigningKey` **never** in `appsettings` (empty value in the file); user-secrets in dev, Secrets Manager on AWS; `ValidateOnStart` refuses to start with a missing key or one shorter than 32 chars | `AuthorizationTests.Api_RefusesToStart_WhenJwtSigningKeyIsTooShort` |
| Authorization | `FallbackPolicy = RequireAuthenticatedUser` (deny by default); policies `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly` (`RequireRole`); explicit `AllowAnonymous` only on `/health/*`, `/auth/login`, webhooks (own HMAC authentication), OpenAPI/Scalar (Development) | `AuthorizationPolicies`, `Program.cs`, `AuthorizationTests` (200/403/401) |
| Unknown route | anonymous → **401** (the fallback policy applies even without an endpoint: routes are not revealed); authenticated → 404 ProblemDetails | `RequestPipelineTests` |
| Login | `POST /api/v1/auth/login`: native .NET 10 validation (`AddValidation`, DataAnnotations) → 400 ProblemDetails; failure → an **identical 401** for unknown e-mail, wrong password and inactive user (`auth.invalid_credentials`), with a hash check against a *decoy* when there is no user (same cost → no timing oracle) | `LoginHandler`, `AuthEndpointsTests.Login_WrongPassword_UnknownEmail_AndInactiveUser_AreIndistinguishable` |
| Rate limiting | `AddRateLimiter`: fixed window of 5 attempts/min per source IP on login → 429; 1200/min per IP on webhooks; sliding window 60/min per user on `POST /orders` (D-84). The limiter runs after authentication (so it can partition by user) and before authorization | `ApiSecurityServiceCollectionExtensions`, `RateLimitOptions`; `AuthEndpointsTests.Login_IsRateLimitedPerClient`, `PlaceOrderRateLimitTests` |
| Logs | events `3000/3001` with `UserId` (success) or an internal reason (`UnknownUser/WrongPassword/InactiveUser/MalformedEmail`) — **no e-mail, password or token**; EF without `EnableSensitiveDataLogging` | `LogMessagePrivacyTests` |
| Principal in the application | `ICurrentUser` (Application) built from the claims per request (`HttpContextCurrentUser`); use cases do resource authorization from it | `GET /api/v1/me`, `OrderAccessAndCancelTests` |
| Admin | `GET /api/v1/users/{id}` (AdminOnly); `/api/v1/admin/outbox` (AdminOnly) | `UsersEndpoints`, `OutboxAdminEndpoints`, `AuthorizationTests` |
| Seed | `dotnet run --project src/FulfillmentHub.Api -- seed`: Development only, passwords from user-secrets (`Seed:*Password`, ≥ 12 chars), fictional data, idempotent | `DevelopmentSeeder` |

## 2b. Payment and delivery webhooks — Implemented (Phases 5 and 7, 2026-09-18)

| Item | Implementation | Evidence |
|---|---|---|
| Authenticity | `X-Signature` = HMAC-SHA256 hex of the **raw body** with `Providers:Payment:WebhookSigningKey` (≥ 16 chars, outside the code, validated at startup); constant-time comparison (`CryptographicOperations.FixedTimeEquals`); malformed or missing hex = invalid | `WebhookSignatureVerifier`, `Webhook_WithBadSignature_IsRejected_AndNothingIsPersisted` (wrong key / no header / tampered body → 401) |
| Replay | `X-Timestamp` (unix s) with a 5 min window (`WebhookTimestampToleranceSeconds`); then dedup by `UNIQUE(provider, provider_event_id)` in `webhook_events` — the second delivery gets a 200 without effect | same test (timestamp −10 min → 401); `DuplicateWebhook_IsAcknowledged_ButAppliedOnce` |
| A leaked key does not confirm a payment (D-42) | `paid`/`refunded` are only applied after a `GET` on the provider; the status in the webhook body is only a trigger | `Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted` |
| Flood / large body | own `webhooks` rate limit policy (`RateLimiting:WebhooksPerMinute` per IP, 429); `Content-Length`/read limited to 64 KB (413); the body is read once, in memory, with a `CancellationToken` | `ApiSecurityServiceCollectionExtensions`, `WebhookReceiver` |
| Malformed | invalid JSON or no `data` with a valid signature → 400 ProblemDetails, nothing persisted | `Webhook_WithValidSignature_ButMalformedPayload_Returns400` |
| Logs | events 5200–5203: provider, event id, rejection reason — **never** the body, the signature or the key; the payload lives only in `webhook_events.payload` (jsonb) | `LogMessagePrivacyTests`; manual host log review on 2026-09-18 |
| Secrets | `Providers:Payment:ApiKey` and `WebhookSigningKey` empty in `appsettings`; user-secrets in dev (Api and Worker); the simulator ships **dev-only** values only in `appsettings.Development.json` | `PaymentProviderOptions` (`ValidateOnStart`), `.gitignore` |

Phase 7: the same pipeline (`WebhookReceiver`) receives `event.delivery_status` on `POST /api/v1/webhooks/deliveries` with
**its own key and header** (`Providers:Delivery:WebhookSigningKey`, `X-Uber-Signature`): the payment provider's key does
not sign delivery events (`Webhook_WithBadSignature_OrWrongHeader_IsRejected`). Delivery events are **not** confirmed with
a `GET` (D-59, residual risk R4).

## 3. Secrets and configuration

| Environment | Mechanism | Rules |
|---|---|---|
| Local | `dotnet user-secrets` (Api, Worker) and/or an ignored `.env` (compose); `.env.example` without real values | `appsettings*.json` never contain secrets; the simulator keys are obvious development values (`dev-only-...`) |
| Tests | values generated per test / Testcontainers | no repository secrets needed by CI |
| CI | no secrets at all; `GITHUB_TOKEN` with least privilege per workflow (`contents: read`, `security-events: write` only for CodeQL) | see `.github/workflows/` |
| AWS (Phases 15–16) | **Secrets Manager** (connection string, JWT key, HMAC keys) injected into the task definition through `secrets`; Parameter Store for non-sensitive config | task role with `secretsmanager:GetSecretValue` restricted to the ARN |

Forbidden anywhere: secrets in code, commits, logs, URLs, error messages, OpenAPI, workflow files.

## 4. OWASP Top 10 (2021) — checklist with evidence

| # | Category | Status | Evidence / gap |
|---|---|---|---|
| A01 | Broken Access Control | **Implemented** | `FallbackPolicy` deny-by-default; role policies; resource authorization (`OrderQueries.Visible()`, `CancelOrderHandler`) with 404-not-403; `AuthorizationTests`, `OrderAccessAndCancelTests`, `RequestPipelineTests` |
| A02 | Cryptographic Failures | **Partial** | PBKDF2-HMAC-SHA512 ✔; JWT key ≥ 32 bytes validated at startup ✔; `ValidAlgorithms=[HS256]` ✔; HMAC-SHA256 constant-time ✔; `no-store` on API responses ✔; HSTS registered ✔ — **gap**: TLS itself is the load balancer's job (Phase 16) |
| A03 | Injection | **Implemented** | EF Core parameterised; only `FromSqlInterpolated`; no `*Raw(`; native request validation; CodeQL `security-extended` includes injection queries (§6) |
| A04 | Insecure Design | **Implemented** | threat model (§1) with trust boundaries and residual risks; idempotency per user (ADR-010); limits (1–50 items, 1–99 units, 64 KB / 256 KB bodies, rate limits); reconciliation jobs; outbox at-least-once with idempotent consumers |
| A05 | Security Misconfiguration | **Partial** | security headers ✔ (`SecurityHeadersTests`); no stack traces outside Development (ProblemDetails) ✔; OpenAPI/Scalar only in Development ✔; CORS not enabled by decision ✔; non-root containers ✔ (`USER app`, Phase 13) — **gap**: `AllowedHosts` and `ForwardedHeaders` are only meaningful behind the load balancer (Phase 16, BL-113) |
| A06 | Vulnerable and Outdated Components | **Implemented** (CI executed 2026-09-21) | CPM pinned versions; `dotnet list package --vulnerable --include-transitive` gate in `ci.yml`; dependency review on PRs; .NET 10 LTS; image scanning with Trivy (HIGH/CRITICAL, `--ignore-unfixed`) on every CI run — 0 findings on 2026-09-21 |
| A07 | Identification and Authentication Failures | **Implemented** · R1/R2 accepted | 5/min per IP; identical 401 + decoy hash; 15-min expiry; `AuthEndpointsTests` |
| A08 | Software and Data Integrity Failures | **Partial** | webhooks signed + verified with the provider ✔; outbox in the same transaction ✔ (`OutboxTests`); NuGet lock files + `--locked-mode` restore ✔ (BL-169); CI with least-privilege `permissions:`, every action pinned to a full commit SHA and the Trivy image pinned by digest ✔ (§6.5) — **gap**: images are not signed / no provenance attestation (revisit with the ECR push, Phase 16) |
| A09 | Security Logging and Monitoring Failures | **Partial** | structured auth events 3000/3001 ✔; rejected webhooks 5200 + `fh.webhooks.rejected{reason}` ✔; PII guard test ✔; **alerts** on 5xx / latency / worker heartbeat / outbox backlog ✔ (OBSERVABILITY.md §6) — **gap**: alert on login failures and rejected webhooks documented but not provisioned (D-79) |
| A10 | Server-Side Request Forgery | **Implemented (by design)** | no user-supplied URL is ever fetched; provider URLs are validated configuration (T8) |

## 5. Personal data (Brazilian LGPD — data minimization)
- Collect only what is needed (name, e-mail, phone, delivery address). Logs carry ids only (T9).
- Data sent to the "provider" limited to what the delivery needs (simulated).
- Retention: processed `webhook_events` and `outbox_messages` may be purged after 30 days (P2 job). `idempotency_records` expire after 24 h.
- No real data: seeds use fictional data.

## 6. SAST and dependency scanning

| Layer | Tool | Where | Status |
|---|---|---|---|
| Compiler analyzers (not SAST) | .NET analyzers `latest-recommended` incl. `CA3xxx`/`CA5xxx`, EF analyzers, `TreatWarningsAsErrors` | every build, `ci.yml` | **Implemented** |
| **SAST** | **GitHub CodeQL** (C#, `security-extended` suite) | `.github/workflows/codeql.yml` — push to `main`, pull requests, weekly schedule; results in *Security → Code scanning* | **Implemented and executed** — first run 2026-09-21 ([run 35605030657](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35605030657), 2 min 58 s, 63 rules, **1 finding**, triaged — see §6.4) |
| Vulnerable packages | `dotnet list package --vulnerable --include-transitive` (fails the job on any hit) | `ci.yml` | **Implemented and executed** (0 advisories on 2026-09-21) |
| Dependency review | `actions/dependency-review-action` (fails PRs that add known-vulnerable packages) | `ci.yml`, pull requests only | **Implemented** — not exercised yet: the job only runs on pull requests and none has been opened (it shows as `skipped` on push runs) |
| Secret scanning | gitleaks (full history) | `ci.yml` | **Implemented and executed** (0 leaks on 2026-09-21) — plus GitHub's native secret scanning/push protection (repository setting, recommended) |
| Image scanning | Trivy `image --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1` on the three images built on the runner | `ci.yml`, job `images` (after the size gate, before the E2E) | **Implemented and executed** (0 HIGH/CRITICAL on 2026-09-21, [run 35623513605](https://github.com/Gabriel-PereiraL/fullfillmentHub/actions/runs/35623513605)) |

### 6.1 How CodeQL works here
1. `codeql.yml` checks out the repository, installs .NET 10, initialises CodeQL for `csharp` with the `security-extended`
   query pack, runs an explicit `dotnet build` (so the extractor sees every compiled file with the real SDK), then
   `analyze` uploads SARIF to the repository's code-scanning store.
2. Results appear under **Security → Code scanning alerts**; on pull requests the same findings are annotated inline
   and the check fails on `error`-level alerts.
3. A weekly run catches new queries shipped by GitHub against unchanged code.

### 6.2 Triage procedure for findings
1. Open the alert; read the query id (`cs/...`) and the data-flow path CodeQL shows.
2. Decide: **true positive** → fix in code, reference the alert in the commit; **false positive** → dismiss with reason
   "false positive" and a one-line justification (kept in the alert's history); **won't fix** → only with an entry in
   §1.6 (residual risks).
3. Never suppress a query pack-wide in `codeql-config` to make a run green; per-alert dismissal keeps the audit trail.
4. Record notable outcomes in `DECISIONS.md` when they change a control.

### 6.3 Limitations
- CodeQL analyses source we compile; NuGet packages are covered by dependency review/`--vulnerable`, not by CodeQL.
- `security-extended` raises more low-confidence findings than the default suite; the triage rule above applies.
- Runs only on GitHub-hosted runners (no local execution documented; the CodeQL CLI can reproduce it, not required).
- Test projects are analysed too; findings there are triaged with the same rules.

### 6.4 Analysis results (first run, 2026-09-21)
| # | Query | Location | Triage |
|---|---|---|---|
| 1 | `cs/log-forging` (medium) — "log entries created from user input" | `CorrelationIdMiddleware.cs:23` — the `X-Correlation-Id` value goes into a logging scope | **False positive, dismissed with justification** (alert #1): `IsAcceptable` whitelists the value to 1–64 ASCII letters/digits/`-`/`_` before it reaches the scope and replaces anything else with a server-generated GUID v7, so no CR/LF or control character can reach a log line. CodeQL does not recognise the custom whitelist as a sanitizer. Kept as is: the id must be logged verbatim to correlate a customer's ticket. |

Open alerts after triage: **0**. Every future finding follows §6.2.

### 6.5 Supply-chain pinning in the workflows (2026-09-21)
Everything the pipeline executes is pinned to an immutable reference, so a moved tag upstream cannot change what runs:

| Dependency | Reference in the workflow | Corresponds to |
|---|---|---|
| `actions/checkout` | `fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09` | v5.1.0 |
| `actions/setup-dotnet` | `26b0ec14cb23fa6904739307f278c14f94c95bf1` | v5.4.0 |
| `actions/upload-artifact` | `330a01c490aca151604b8cf639adc76d48f6c5d4` | v5.0.0 |
| `actions/dependency-review-action` | `2031cfc080254a8a887f58cffee85186f0e49e48` | v4.9.0 (head of the `v4` branch) |
| `gitleaks/gitleaks-action` | `ff98106e4c7b2bc287b24eaf42907196329070c7` | v2.3.9 |
| `github/codeql-action/{init,analyze}` | `1c5b675653bb5c22dbe9b12b556ec555138e09fd` | v4.38.1 |
| `aquasec/trivy` (Docker image) | `0.74.0@sha256:62b1e65e8869bc4b4c6aa4fa2b21595256c7c2f6018a9d9ad61caf87187c1969` | the digest `latest` resolved to in run 35624106304 |

The SHAs are the exact commits the major tags (`v5`, `v4`, `v2`) resolved to in the green runs of 2026-09-21 (the
runner log prints `Download action repository '…' (SHA:…)`), so pinning changed nothing about what executes. Rule for
bumps: change the SHA and the trailing `# vX.Y.Z` comment together after reading the upstream release notes; Dependabot
for `github-actions` is the natural automation and is a P3 item (BL-249). The base images in `docker/Dockerfile.*` are
still referenced by tag (`10.0-alpine`); Trivy in CI is the control there, and a digest pin is part of the same item.

## 7. Pre-publication checklist (before any push/publication)
See `DEPLOYMENT.md`, section "Leak-prevention checklist".

## 8. Security decisions
- D-42 (payment webhooks verified with a `GET`), D-59 (delivery webhooks not verified), D-69 (webhook rate limit),
  D-82 (headers / no CORS / HTTPS at the edge), D-83 (Kestrel body limit), D-84 (`POST /orders` per-user limit),
  D-85 (PII guard test), D-86 (readiness without the broker), D-87 (dispositions), D-90 (immutable references in
  the workflows) — all in `DECISIONS.md`.
- Open (P2): progressive login lockout, rotating refresh token, per-webhook HMAC key rotation. (The NuGet lock file, BL-169, closed on 2026-09-21.)
