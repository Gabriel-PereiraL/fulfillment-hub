# ADR-007 — Minimal APIs, ProblemDetails, validação nativa, OpenAPI + Scalar

**Status**: aceita · **Data**: 2026-09-18

## Contexto
Precisamos de uma API HTTP moderna, documentada, com erros padronizados e validação de entrada, com o mínimo de dependências.

## Opções
- Controllers MVC vs. **Minimal APIs**.
- Erros ad hoc vs. **ProblemDetails (RFC 9457)**.
- FluentValidation vs. DataAnnotations manual vs. **validação nativa de Minimal APIs do .NET 10** (`AddValidation()`).
- Swashbuckle vs. **`Microsoft.AspNetCore.OpenApi` nativo + Scalar** (UI).
- Auto-discovery de endpoints por reflexão vs. **registro explícito** por módulo.

## Decisão
Minimal APIs com `MapGroup` por módulo e `TypedResults`; `AddProblemDetails()` + `IExceptionHandler`; validação nativa
(.NET 10) com DataAnnotations nos records de request + invariantes no domínio; OpenAPI nativo + Scalar em Development;
registro explícito (`app.MapOrdersEndpoints()`); prefixo fixo `/api/v1`.

## Motivo
Menos dependências, tipagem forte de respostas (`Results<Created<T>, ValidationProblem>`), documentação gerada dos metadados,
erros uniformes. Registro explícito evita mágica de reflexão.

## Trade-offs
- Controllers têm mais material didático; Minimal APIs exigem disciplina de organização (resolvido com classes `*Endpoints`).
- Validação nativa é nova (.NET 10): se faltar recurso (validação assíncrona/condicional), reavaliar FluentValidation com justificativa.
- Scalar é dependência de UI só em dev.

## Consequências
- Todo endpoint tem `WithName/WithSummary/Produces*`, autorização declarada e teste de integração.
- Erros nunca vazam stack/detalhes fora de Development.
