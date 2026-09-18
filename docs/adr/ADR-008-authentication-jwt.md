# ADR-008 — Usuários próprios + JWT bearer + policies

**Status**: aceita · **Data**: 2026-09-18 (implementação: Fase 3)

## Contexto
A API precisa de autenticação e autorização por papel (`Customer`, `Operator`, `Admin`) e a Admin UI (Blazor) precisa reutilizar as mesmas regras.
Queremos demonstrar autenticação/autorização no ASP.NET Core sem delegar a um IdP externo.

## Opções
1. ASP.NET Core Identity completo (tabelas, UI, managers).
2. IdP externo (Auth0/Cognito/Keycloak) com OIDC.
3. **Usuários próprios** (tabelas `users`/`roles`), `PasswordHasher<T>` do Identity (só a classe), JWT emitido pela API.
4. Cookies apenas.

## Decisão
Opção 3 para a API (JWT HS256, chave ≥ 256 bits em secrets, `exp` 15 min, `iss/aud` validados, claims de papel e `customer_id`).
`FallbackPolicy` = autenticado; policies por papel; autorização por recurso nos casos de uso. Admin Blazor: cookie auth com as mesmas policies.
Webhooks: HMAC (não JWT). Refresh token: P2.

## Motivo
Mostra o mecanismo completo (hash, emissão, validação, policies) em código próprio e pequeno; Identity completo traz muito o que não usamos;
IdP externo esconde justamente o que se quer demonstrar e adiciona dependência/custo.

## Trade-offs
- Sem rotação/revogação de token na v1 (exp curta mitiga); sem MFA; sem OAuth para terceiros. Documentado como extensões.
- HS256 exige o mesmo segredo em Api/Admin (aceito; RS256 é P3 se surgir mais de um emissor/validador).

## Consequências
- SECURITY.md registra o desenho e as evidências; testes T16/BL-149 cobrem 401/403/acesso cruzado.
