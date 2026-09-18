# ADR-001 — Modular monolith with three processes

**Status**: accepted · **Date**: 2026-09-18

## Context
One developer, a C#/.NET portfolio project, a product with clear modules (Catalog, Customers, Orders, Payments,
Deliveries, Identity, Operations), simulated external integrations, a need for asynchronous processing.
The goal is to demonstrate defensible engineering, not "LinkedIn architecture".

## Options considered
1. **Modular monolith** (one solution, one database, modules as folders, separate processes only where the lifecycle differs).
2. Microservices (one service per module, separate databases, communication over HTTP/queues).
3. "Classic" monolith in a single process (API + workers in the same host).

## Decision
Option 1: a modular monolith with **three processes**: `Api` (HTTP), `Worker` (outbox, consumers, reconciliation) and
`ProviderSimulator` (simulated external systems). `Admin` (Blazor) will be a fourth host in Phase 17.
Layers as projects (`Domain`, `Application`, `Infrastructure`) + folders per module + `ArchitectureTests`.

## Rationale
- Microservices multiply operational cost (deploys, network, distributed observability, consistency) with no gain for one developer.
- Separating the API and the Worker is justified: different scaling and restart profiles, and it is what one does on ECS (two services).
- The Simulator must be a separate process so that the HTTP integration is real (network, timeouts, failures).

## Trade-offs
- A single database = schema coupling between modules (accepted; communication between modules through events/outbox where it makes sense).
- No fault isolation per module (accepted; resilience is per integration, not per service).

## Consequences
- Architecture tests enforce the dependency direction.
- If a module needs to scale on its own in the future, extraction is possible because the boundaries already exist.
