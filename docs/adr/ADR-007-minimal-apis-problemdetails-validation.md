# ADR-007 — Minimal APIs, ProblemDetails, native validation, OpenAPI + Scalar

**Status**: accepted · **Date**: 2026-09-18

## Context
We need a modern, documented HTTP API with standardized errors and input validation, with the minimum of dependencies.

## Options
- MVC controllers vs. **Minimal APIs**.
- Ad hoc errors vs. **ProblemDetails (RFC 9457)**.
- FluentValidation vs. manual DataAnnotations vs. **the native Minimal APIs validation of .NET 10** (`AddValidation()`).
- Swashbuckle vs. **native `Microsoft.AspNetCore.OpenApi` + Scalar** (UI).
- Endpoint auto-discovery by reflection vs. **explicit registration** per module.

## Decision
Minimal APIs with a `MapGroup` per module and `TypedResults`; `AddProblemDetails()` + `IExceptionHandler`; native validation
(.NET 10) with DataAnnotations on the request records + invariants in the domain; native OpenAPI + Scalar in Development;
explicit registration (`app.MapOrdersEndpoints()`); fixed `/api/v1` prefix.

## Rationale
Fewer dependencies, strongly-typed responses (`Results<Created<T>, ValidationProblem>`), documentation generated from metadata,
uniform errors. Explicit registration avoids reflection magic.

## Trade-offs
- Controllers have more learning material; Minimal APIs require organizational discipline (solved with `*Endpoints` classes).
- Native validation is new (.NET 10): if a feature is missing (asynchronous/conditional validation), re-evaluate FluentValidation with a justification.
- Scalar is a UI dependency in dev only.

## Consequences
- Every endpoint has `WithName/WithSummary/Produces*`, declared authorization and an integration test.
- Errors never leak stack traces/details outside Development.
