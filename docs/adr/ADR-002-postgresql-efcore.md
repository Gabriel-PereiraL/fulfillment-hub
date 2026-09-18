# ADR-002 — PostgreSQL + EF Core (Npgsql)

**Status**: accepted · **Date**: 2026-09-18

## Context
A relational database is a requirement. We need strong constraints, `jsonb` for payloads, optimistic concurrency, `SKIP LOCKED`
for the outbox, and a mature EF Core provider. In the cloud, RDS.

## Options
1. **PostgreSQL** (Npgsql.EntityFrameworkCore.PostgreSQL).
2. SQL Server (more "at home" in the .NET ecosystem; heavier licence/image; more expensive on RDS).
3. MySQL/MariaDB (the author's previous experience; Pomelo provider; features such as `SKIP LOCKED`/`jsonb` less mature).

## Decision
PostgreSQL 17 with EF Core 10 + Npgsql.

## Rationale
`jsonb`, `xmin` as the concurrency token, `FOR UPDATE SKIP LOCKED`, partial indexes, sequences, excellent Testcontainers
support and cheap RDS (db.t4g.micro). It shows reviewers that .NET is not tied to SQL Server.

## Trade-offs
- Less "classic" for corporate SQL Server .NET jobs (accepted; EF Core abstracts most of it).
- `timestamp with time zone` requires UTC with Npgsql (rule: always UTC).

## Consequences
- Mappings use Postgres features deliberately and document them (`UseXminAsConcurrencyToken`, `jsonb`, partial indexes).
- Integration tests use a real Postgres (Testcontainers); the in-memory provider is forbidden.
