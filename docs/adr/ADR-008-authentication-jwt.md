# ADR-008 — Own users + JWT bearer + policies

**Status**: accepted · **Date**: 2026-09-18 (implementation: Phase 3)

## Context
The API needs authentication and role-based authorization (`Customer`, `Operator`, `Admin`) and the Admin UI (Blazor) must reuse the same rules.
We want to demonstrate authentication/authorization in ASP.NET Core without delegating to an external IdP.

## Options
1. Full ASP.NET Core Identity (tables, UI, managers).
2. External IdP (Auth0/Cognito/Keycloak) with OIDC.
3. **Own users** (`users`/`roles` tables), Identity's `PasswordHasher<T>` (the class only), JWT issued by the API.
4. Cookies only.

## Decision
Option 3 for the API (JWT HS256, key ≥ 256 bits in secrets, `exp` 15 min, validated `iss/aud`, role and `customer_id` claims).
`FallbackPolicy` = authenticated; per-role policies; resource authorization in the use cases. Admin Blazor: cookie auth with the same policies.
Webhooks: HMAC (not JWT). Refresh token: P2.

## Rationale
Shows the whole mechanism (hashing, issuance, validation, policies) in small in-house code; full Identity brings a lot we do not use;
an external IdP hides precisely what we want to demonstrate and adds a dependency/cost.

## Trade-offs
- No token rotation/revocation in v1 (the short expiry mitigates it); no MFA; no OAuth for third parties. Documented as extensions.
- HS256 requires the same secret in Api/Admin (accepted; RS256 is P3 if more than one issuer/validator appears).

## Consequences
- SECURITY.md records the design and the evidence; tests T16/BL-149 cover 401/403/cross access.
