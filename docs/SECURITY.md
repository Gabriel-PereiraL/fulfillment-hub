# SECURITY — FulfillmentHub

Security is a first-class requirement. This document is the plan and, from Phase 10 on, the **evidence record**
(each item points to code and tests). Nothing here claims "implemented" before it exists.

## 1. Simplified threat model

### Assets
Customer data (name, e-mail, phone, address), orders and amounts, user credentials (hashes), integration secrets (HMAC
keys, provider credentials, JWT signing key, connection string), integrity of order/payment/delivery state, availability
of the API and the worker.

### Attack surface
| Entry point | Who reaches it | Main risk |
|---|---|---|
| Public API (`/orders`, `/auth`) | authenticated customers / anonymous (login) | credential stuffing, IDOR / broken access control, abuse (rate), injection, over-posting |
| Webhooks (`/webhooks/*`) | "providers" (anyone who knows the URL) | forged events (a "paid" payment), replay, flood |
| Admin UI | operators/admins | privilege escalation, CSRF (Blazor Server), data exposure |
| Database / queue / secrets | infrastructure | leaked credentials, unintended network access |
| Dependency chain | NuGet, base images | known vulnerabilities |
| Repository | public | secrets or local files leaked into the history |

### Actors
A malicious authenticated user (customer), an anonymous attacker on the internet, an insider with repository/CI access, a compromised dependency.

### Prioritized threats (condensed STRIDE)
| Threat | Category | Planned mitigation | Evidence (phase) |
|---|---|---|---|
| Forged webhook confirms a payment | Spoofing/Tampering | HMAC-SHA256 with a key per provider, constant-time comparison, timestamp window (5 min), dedup by event id, **and** `paid`/`refunded` confirmed with a `GET` on the provider before being applied (D-P5 = yes, **implemented in Phase 5**, see §2b) | 5 ✔ /7/10 |
| A customer reads/cancels another customer's order | Elevation / Information disclosure | resource authorization inside the use case (`order.CustomerId == principal.CustomerId`), 404 instead of 403 so existence is not revealed; T16 tests | **4 ✔** (`OrderQueries.Visible()`, `CancelOrderHandler`; `Customer_CannotSeeOrCancel_AnotherCustomersOrder`) |
| Credential stuffing on login | Spoofing/DoS | rate limit per IP + account, PBKDF2 hashing (`PasswordHasher`), generic messages, progressive lockout (P2) | **3 ✔** (rate limit per IP, PBKDF2, identical 401 + decoy; per-account lockout pending) |
| Stolen JWT | Spoofing | short expiry (15 min), `aud`/`iss` validated, HTTPS mandatory outside dev, no tokens in logs/URLs; rotating refresh token (P2) | **3 ✔** (15 min expiry, iss/aud/alg validated, no tokens in logs; HTTPS/HSTS in Phase 10) |
| Over-posting (`status`, `total`, `customerId` in the body) | Tampering | request DTOs without those fields; server-derived values; tests | **4 ✔** (`PlaceOrderRequest`; extra fields ignored, dedicated test) |
| SQL injection | Tampering | parameterized EF Core; `FromSql` only interpolated; no concatenation; EF analyzer | 2 |
| Webhook/order flood | DoS | rate limiting (`AddRateLimiter`), body size limit, timeouts, the queue absorbs peaks | 10 |
| SSRF | Tampering | the system **never** calls user-supplied URLs; provider URLs are validated configuration (host allowlist) | 10 |
| PII leaking into logs | Information disclosure | redaction (masked e-mail/phone), never log full webhook bodies or order payloads, `LoggerMessage` with explicit fields | 10 |
| Secrets in the repository | Information disclosure | user-secrets/env locally, Secrets Manager in the cloud, gitleaks in CI, pre-publication checklist | 1/14/20 |
| Vulnerable dependency | Supply chain | `dotnet list package --vulnerable`, dependency review, Trivy on images, CPM with pinned versions | 10/14 |
| Automatic destructive migrations | Tampering/Availability | never an automatic `Migrate()`; reviewed idempotent script; one-off task | 16 |
| Network access to the database/queue | Information disclosure | RDS in a private subnet, security group open only to ECS, least-privilege IAM task role, no public IP on the database | 15 |

## 2. Authentication and authorization — IMPLEMENTED (Phase 3, 2026-09-18)

| Item | Implementation | Evidence |
|---|---|---|
| Users | own `users` table (`User` aggregate, fixed roles `Customer/Operator/Admin` in `text[]`); the ASP.NET Identity framework is **not** used, only the `PasswordHasher<User>` class (ADR-008) | `src/FulfillmentHub.Domain/Identity/User.cs`, `src/FulfillmentHub.Infrastructure/Identity/IdentityPasswordHasher.cs` |
| Password hashing | `PasswordHasher<TUser>` v3: PBKDF2-HMAC-SHA512, per-password salt, 100k iterations, versioned format; transparent re-hash at login when the format evolves (`SuccessRehashNeeded`); a malformed hash in the database means "wrong password", never a 500 | `IdentityPasswordHasherTests`, `LoginHandler` |
| Token issuance | `JsonWebTokenHandler` (the current IdentityModel stack), HS256 with a key ≥ 32 bytes; **minimal** claims: `sub` (user id), `role[]`, `customer_id` (customers only), `jti`, `iat/nbf/exp/iss/aud`. No e-mail or name in the token | `JwtTokenService`, `JwtTokenServiceTests` |
| Expiry | 15 min (`Jwt:AccessTokenLifetimeMinutes`, range 1–60), 30 s clock skew; no refresh token in v1 (P2, BL-033) | `JwtOptions`, `AuthEndpointsTests.ExpiredToken_Returns401` |
| Token validation | `AddJwtBearer` with issuer, audience, signature (`ValidAlgorithms = [HS256]`), lifetime and `RequireExpirationTime`; `MapInboundClaims = false` (short names, no magic mapping) | `JwtBearerOptionsSetup`, tampered/expired/foreign-key token tests |
| Secret | `Jwt:SigningKey` **never** in `appsettings` (empty value in the file); user-secrets in dev, Secrets Manager on AWS; `ValidateOnStart` refuses to start with a missing key or one shorter than 32 chars | `AuthorizationTests.Api_RefusesToStart_WhenJwtSigningKeyIsTooShort` |
| Authorization | `FallbackPolicy = RequireAuthenticatedUser` (deny by default); policies `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly` (`RequireRole`); explicit `AllowAnonymous` only on `/health/*`, `/auth/login`, OpenAPI/Scalar (Development) | `AuthorizationPolicies`, `Program.cs`, `AuthorizationTests` (200/403/401) |
| Unknown route | anonymous → **401** (the fallback policy applies even without an endpoint: routes are not revealed); authenticated → 404 ProblemDetails | `RequestPipelineTests` |
| Login | `POST /api/v1/auth/login`: native .NET 10 validation (`AddValidation`, DataAnnotations) → 400 ProblemDetails; failure → an **identical 401** for unknown e-mail, wrong password and inactive user (`auth.invalid_credentials`), with a hash check against a *decoy* when there is no user (same cost → no timing oracle) | `LoginHandler`, `AuthEndpointsTests.Login_WrongPassword_UnknownEmail_AndInactiveUser_AreIndistinguishable` |
| Rate limiting | `AddRateLimiter`: fixed window of 5 attempts/min per source IP on login → 429 (`RejectionStatusCode`) | `ApiSecurityServiceCollectionExtensions`, `AuthEndpointsTests.Login_IsRateLimitedPerClient` |
| Logs | events `3000/3001` with `UserId` (success) or an internal reason (`UnknownUser/WrongPassword/InactiveUser/MalformedEmail`) — **no e-mail, password or token**; EF without `EnableSensitiveDataLogging` (parameters are not logged) | manual host log review on 2026-09-18 |
| Principal in the application | `ICurrentUser` (Application) built from the claims per request (`HttpContextCurrentUser`); use cases do resource authorization from it (Phase 4) | `GET /api/v1/me` |
| Admin | `GET /api/v1/users/{id}` (AdminOnly) exposes a user's e-mail/roles/status — the only administrative endpoint of that phase | `UsersEndpoints` |
| Seed | `dotnet run --project src/FulfillmentHub.Api -- seed`: Development only, passwords from user-secrets (`Seed:*Password`, ≥ 12 chars), fictional data, idempotent | `DevelopmentSeeder` |

### Known limitations (on record)
- No token refresh/revocation: a leaked token is valid for up to 15 min (BL-033, P2).
- No progressive per-account lockout (only per-IP rate limiting): a distributed attacker can try 5/min per IP (P2).
- Rate limiting by `RemoteIpAddress`: behind the ALB (Phase 16) it requires `ForwardedHeaders` configured with known proxies, otherwise everyone shares the load balancer's IP (BL-106).
- HS256 shares the same key between issuer and validator (Api/Admin); RS256 only if more than one issuer appears (ADR-008).
- No MFA, no OAuth/OIDC for third parties (out of scope, ADR-008).

### Original design (kept for reference)
- **Own users** (`users` table), passwords with `PasswordHasher<User>` (PBKDF2-HMAC-SHA512, ASP.NET Core Identity's default iterations — the class only, not the framework). Policy: ≥ 12 characters (no silly composition rules).
- **JWT bearer** issued by `POST /auth/login`: HS256 with a ≥ 256-bit key from secrets; claims `sub`, `role[]`, `customer_id` (when applicable), `jti`; `exp` 15 min; fixed, validated `iss`/`aud`; 30 s clock skew.
- **Authorization**: `FallbackPolicy` = authenticated user (deny by default). Policies: `CustomerOnly`, `OperatorOrAdmin`, `AdminOnly`. **Resource** authorization inside the use case (the principal is passed as `ICurrentUser`), with tests.
- Admin UI (Blazor Server): cookie auth with `SameSite=Strict`, native antiforgery, the same policies (Phase 17).

## 2b. Payment and delivery webhooks — IMPLEMENTED (Phases 5 and 7, 2026-09-18)

| Item | Implementation | Evidence |
|---|---|---|
| Authenticity | `X-Signature` = HMAC-SHA256 hex of the **raw body** with `Providers:Payment:WebhookSigningKey` (≥ 16 chars, outside the code, validated at startup); constant-time comparison (`CryptographicOperations.FixedTimeEquals`); malformed or missing hex = invalid | `WebhookSignatureVerifier`, `Webhook_WithBadSignature_IsRejected_AndNothingIsPersisted` (wrong key / no header / tampered body → 401) |
| Replay | `X-Timestamp` (unix s) with a 5 min window (`WebhookTimestampToleranceSeconds`); then dedup by `UNIQUE(provider, provider_event_id)` in `webhook_events` — the second delivery gets a 200 without effect | same test (timestamp −10 min → 401); `DuplicateWebhook_IsAcknowledged_ButAppliedOnce` |
| A leaked key does not confirm a payment (D-P5) | `paid`/`refunded` are only applied after a `GET` on the provider; the status in the webhook body is only a trigger | `Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted` (a valid "paid" webhook for a declined payment → the order stays cancelled) |
| Flood / large body | own `webhooks` rate limit policy (`RateLimiting:WebhooksPerMinute` per IP, 429); `Content-Length`/read limited to 64 KB (413); the body is read once, in memory, with a `CancellationToken` | `ApiSecurityServiceCollectionExtensions`, `WebhookReceiver` |
| Malformed | invalid JSON or no `data` with a valid signature → 400 ProblemDetails, nothing persisted | `Webhook_WithValidSignature_ButMalformedPayload_Returns400` |
| Logs | events 5200–5203: provider, event id, rejection reason — **never** the body, the signature or the key; the payload lives only in `webhook_events.payload` (jsonb) | manual host log review on 2026-09-18 (0 occurrences of keys/tokens) |
| Secrets | `Providers:Payment:ApiKey` and `WebhookSigningKey` empty in `appsettings`; user-secrets in dev (Api and Worker); the simulator ships **dev-only** values only in `appsettings.Development.json` | `PaymentProviderOptions` (`ValidateOnStart`), `.gitignore` |

Phase 7: the same pipeline (`WebhookReceiver`) receives `event.delivery_status` on `POST /api/v1/webhooks/deliveries` with
**its own key and header** (`Providers:Delivery:WebhookSigningKey`, `X-Uber-Signature`): the payment provider's key does
not sign delivery events (`Webhook_WithBadSignature_OrWrongHeader_IsRejected`). Delivery events are **not** confirmed with
a `GET` (D-59): an event forged with a leaked key can at most advance the delivery/order status (no money involved) and
never regress it; reconciliation with the provider corrects divergences.

Limitations on record: a single HMAC key per provider, no rotation (P2); the timestamp window depends on a synchronized
clock (NTP on the host); events marked `Failed` are corrected by reconciliation rather than re-processed by a sweep.

## 3. Secrets and configuration

| Environment | Mechanism | Rules |
|---|---|---|
| Local | `dotnet user-secrets` (Api, Worker, Admin) and/or an ignored `.env` (compose); `.env.example` without real values | `appsettings*.json` never contain secrets; the simulator keys are obvious development values (`dev-only-...`) |
| Tests | values generated per test / Testcontainers | |
| AWS | **Secrets Manager** (connection string, JWT key, HMAC keys) injected into the task definition through `secrets` (not `environment`); Parameter Store for non-sensitive config | task role with `secretsmanager:GetSecretValue` restricted to the ARN; manual rotation documented |

Forbidden anywhere: secrets in code, commits, logs, URLs, error messages, OpenAPI.

## 4. OWASP Top 10 (2021) — checklist (to be filled with evidence in Phase 10)

| # | Category | Application in this project | Status |
|---|---|---|---|
| A01 | Broken Access Control | deny by default, policies, resource authorization, T16 tests, 404 vs 403 | **implemented (Phases 3–4)**: `FallbackPolicy` ✔, role policies ✔, resource authorization in `OrderQueries`/`CancelOrderHandler` (another customer's order → 404, no stock released) with `OrderAccessAndCancelTests` ✔ |
| A02 | Cryptographic Failures | PBKDF2 for passwords, HMAC-SHA256 webhooks, TLS outside dev, JWT key ≥ 256 bits, no "none" algorithms | **partial (Phase 3)**: PBKDF2-HMAC-SHA512 ✔, JWT key ≥ 32 bytes validated at startup ✔, `ValidAlgorithms=[HS256]` ✔; TLS pending; HMAC-SHA256 webhooks with constant-time comparison ✔ (Phase 5) |
| A03 | Injection | parameterized EF Core, input validation, no dynamic SQL, no `Process.Start` | planned |
| A04 | Insecure Design | threat model, idempotency, limits (items per order, body size), reconciliation | **partial (Phase 4)**: real idempotency keyed per user ✔ (ADR-010), limits of 1–50 items / 1–99 units ✔, over-posting: DTOs without `status/total/customerId` and the `PlaceOrder_IgnoresServerControlledFields` test ✔; body limit and reconciliation pending |
| A05 | Security Misconfiguration | headers (`X-Content-Type-Options`, `Referrer-Policy`, CSP on the Admin), explicit CORS, no stack traces outside dev, OpenAPI only in dev, non-root containers | planned |
| A06 | Vulnerable Components | CPM, `--vulnerable`, dependency review, Trivy, updated base images | planned |
| A07 | Identification & Authentication Failures | login rate limit, generic messages, short expiry, no user enumeration | **implemented (Phase 3)**: 5/min per IP ✔, identical 401 + decoy hash ✔, 15 min expiry ✔, `AuthEndpointsTests` |
| A08 | Software & Data Integrity Failures | webhook signatures, outbox (event integrity), package lockfile, CI with minimal permissions | **partial (Phase 5)**: webhooks signed + verified with the provider ✔ (§2b); outbox Phase 8; lockfile/CI Phase 14 |
| A09 | Security Logging & Monitoring | structured auth logs (success/failure), rejected webhooks, alerts on anomalous 401/403 and DLQ, no PII | **partial (Phases 3/5)**: login events 3000/3001 ✔; 5200 rejected webhook + metric `fh.webhooks.rejected{reason}` ✔; alerts in Phases 11/16 |
| A10 | SSRF | no user-supplied URL is ever called; provider hosts in a configuration allowlist | planned |

## 5. Personal data (Brazilian LGPD — data minimization)
- Collect only what is needed (name, e-mail, phone, delivery address). Phone/e-mail masked in logs and in the Admin (full display only for Admin, with a reason).
- Data sent to the "provider" limited to what the delivery needs (simulated).
- Retention: processed `webhook_events` and `outbox_messages` may be purged after 30 days (P2 job). `idempotency_records` expire after 24 h.
- No real data: seeds use fictional data.

## 6. SAST / dependency scanning (Phases 10/14)
- Local: the .NET security analyzers (`CA3xxx`, `CA5xxx` under `AnalysisLevel latest-recommended`), `dotnet list package --vulnerable --include-transitive`.
- CI (once GitHub Actions exists): CodeQL (C#), GitHub dependency review, gitleaks (secrets), Trivy (images), `dotnet format --verify-no-changes`.

## 7. Pre-publication checklist (used in Phase 20 and before any push)
See `DEPLOYMENT.md`, section "Leak-prevention checklist".

## 8. Pending security decisions
- ~~**D-P5**~~ — **resolved (Phase 5): yes** for `paid`/`refunded` (implemented in `ApplyPaymentWebhookHandler`); delivery events (Phase 7) are not reconciled per webhook.
- Progressive login lockout (P2). Rotating refresh token (P2). Per-webhook HMAC key with rotation (P2).
