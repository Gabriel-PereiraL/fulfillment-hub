# ADR-013 — .NET 10 (LTS) and C# 14

**Status**: accepted · **Date**: 2026-09-18

## Context
The project must use the current LTS version of .NET available at the start. The machine has SDK 10.0.400 (runtime 10.0.11).
.NET 10 is LTS (released in November 2025, 3 years of support). .NET 11 is in preview and is not LTS.

## Decision
Target `net10.0` in every project, the SDK's default `LangVersion` (C# 14), `global.json` pinning `10.0.x` with `rollForward: latestPatch`.
Libraries: stable versions compatible with .NET 10 (EF Core 10, Npgsql 10, OpenTelemetry 1.x, Microsoft.Extensions.Http.Resilience 10.x, xUnit v3, Testcontainers 4.x) — verified on NuGet in Phase 1 and pinned through Central Package Management.

## Rationale
LTS = stability and a maturity signal for reviewers; C# 14 brings `field`, extension members and improvements that the main skill uses sparingly.
Relevant new .NET 10 features: native validation in Minimal APIs, OpenAPI improvements, `Guid.CreateVersion7` (since .NET 9).

## Trade-offs
- Third-party material is still mostly .NET 8/9 (irrelevant for the APIs used).
- `mcr.microsoft.com/dotnet/aspnet:10.0` images available; ECS/Fargate are agnostic.

## Consequences
- No previews (.NET 11) adopted during the project.
- Automatic patch updates through `rollForward`; minor package upgrades reviewed in `Directory.Packages.props`.
