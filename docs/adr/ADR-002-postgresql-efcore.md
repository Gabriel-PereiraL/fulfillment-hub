# ADR-002 — PostgreSQL + EF Core (Npgsql)

**Status**: aceita · **Data**: 2026-09-18

## Contexto
Banco relacional é requisito. Precisamos de constraints fortes, `jsonb` para payloads, concorrência otimista, `SKIP LOCKED`
para outbox, e um provider EF Core maduro. Em nuvem, RDS.

## Opções
1. **PostgreSQL** (Npgsql.EntityFrameworkCore.PostgreSQL).
2. SQL Server (mais "casa" no ecossistema .NET; licença/imagem mais pesada; RDS mais caro).
3. MySQL/MariaDB (experiência prévia do autor; provider Pomelo; menos recursos como `SKIP LOCKED`/`jsonb` maduros).

## Decisão
PostgreSQL 17 com EF Core 10 + Npgsql.

## Motivo
`jsonb`, `xmin` como token de concorrência, `FOR UPDATE SKIP LOCKED`, índices parciais, sequences, ótimo suporte no
Testcontainers e RDS barato (db.t4g.micro). Mostra ao avaliador que .NET não está preso ao SQL Server.

## Trade-offs
- Menos "clássico" para vagas .NET corporativas SQL Server (aceito; EF Core abstrai a maior parte).
- `timestamp with time zone` exige UTC no Npgsql (regra: sempre UTC).

## Consequências
- Mapeamentos usam recursos do Postgres deliberadamente e documentados (`UseXminAsConcurrencyToken`, `jsonb`, índices parciais).
- Testes de integração usam Postgres real (Testcontainers); provider in-memory é proibido.
