# ADR-006 — DbContext through an interface; no generic repository/UoW; no MediatR

**Status**: accepted · **Date**: 2026-09-18

## Context
"Tutorial-style" .NET projects often stack `IRepository<T>`, `IUnitOfWork`, MediatR, AutoMapper and Result everywhere.
We want explicit, testable, idiomatic code without ceremonial abstractions — while keeping the domain free of infrastructure.

## Options
1. Generic repository + UoW on top of EF Core.
2. One repository per aggregate (tactical DDD) with an EF implementation.
3. **`IFulfillmentHubDbContext`** (interface with `DbSet<T>` + `SaveChangesAsync`) used directly by the use cases; `Application` references the EF Core package.
4. Use cases inside `Infrastructure` (no Application layer).

## Decision
Option 3 for data. Use cases are explicit classes (`PlaceOrderHandler`) registered in DI and called directly by the
endpoints — **no MediatR/Mediator**. Interfaces exist only at the external ports (`IDeliveryProviderClient`,
`IPaymentGatewayClient`, `IMessagePublisher`) and on the DbContext. A small in-house `Result` for expected failures; no AutoMapper.

## Rationale
- The `DbContext` **already is** a Unit of Work + Repository; wrapping it duplicates the API, hides LINQ/Include/projections and creates an "anaemic repository".
- MediatR moved to a commercial licence (2025) and, more importantly, it solves no real problem here: direct DI is more traceable.
- Use-case tests run against a real Postgres (Testcontainers), which tests more than repository mocks.

## Trade-offs
- `Application` depends on the EF Core package (not on the provider). Accepted and explicit; `Domain` stays pure.
- No MediatR "pipeline behaviours": cross-cutting concerns (logging, validation, transaction) go into endpoint filters, EF interceptors and decorators only when needed.

## Consequences
- ArchitectureTests: `Domain` does not reference EF; `Application` does not reference `Infrastructure`.
- Copied skills that suggest repository/MediatR are explicitly overridden by the main skill (section 12).
