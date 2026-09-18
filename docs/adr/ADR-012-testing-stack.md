# ADR-012 — Testing stack: xUnit v3, Shouldly, Testcontainers, WebApplicationFactory, NetArchTest

**Status**: accepted · **Date**: 2026-09-18

## Context
Tests must prove behaviour (idempotency, concurrency, outbox, retry, webhooks) with a real database and queue, and enforce the architecture.

## Options
- Framework: **xUnit v3** vs. NUnit vs. MSTest/TUnit.
- Assertions: FluentAssertions (v8 with a commercial licence outside OSS/personal use) vs. **Shouldly** (BSD) vs. AwesomeAssertions (fork) vs. plain `Assert`.
- Database in tests: EF InMemory / SQLite vs. **Testcontainers PostgreSQL**.
- Test doubles: Moq (SponsorLink incident in 2023) vs. NSubstitute vs. **hand-written fakes** (+ NSubstitute if needed).
- Architecture: **NetArchTest.Rules** vs. ArchUnitNET.
- Time: **`FakeTimeProvider`** (Microsoft.Extensions.TimeProvider.Testing).

## Decision
xUnit v3 + Shouldly + Testcontainers (PostgreSQL, LocalStack) + `WebApplicationFactory` + hand-written fakes on the ports + `FakeTimeProvider` + NetArchTest.Rules.
The EF in-memory provider is forbidden in integration tests.

## Rationale
Widely recognized standards, permissive licences, and what really gets tested: constraints, transactions, generated SQL, the full HTTP pipeline.

## Trade-offs
- Integration tests require Docker (locally and in CI) and are slower (mitigated: containers per collection, clean database per test with Respawn).
- Shouldly is less known than FluentAssertions (accepted because of the licence).

## Consequences
- TEST_STRATEGY.md defines the mandatory matrix and the quality rules; every guarantee will link to its test in the final README.
