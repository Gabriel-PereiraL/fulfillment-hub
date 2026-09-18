# ADR-013 — .NET 10 (LTS) e C# 14

**Status**: aceita · **Data**: 2026-09-18

## Contexto
O projeto deve usar a versão LTS atual do .NET disponível no início. Na máquina há o SDK 10.0.400 (runtime 10.0.11).
.NET 10 é LTS (lançado em novembro de 2025, suporte por 3 anos). .NET 11 está em preview e não é LTS.

## Decisão
Target `net10.0` em todos os projetos, `LangVersion` padrão do SDK (C# 14), `global.json` fixando `10.0.x` com `rollForward: latestPatch`.
Bibliotecas: versões estáveis compatíveis com .NET 10 (EF Core 10, Npgsql 10, OpenTelemetry 1.x, Microsoft.Extensions.Http.Resilience 10.x, xUnit v3, Testcontainers 4.x) — verificar no NuGet na Fase 1 e fixar via Central Package Management.

## Motivo
LTS = estabilidade e sinal de maturidade para avaliadores; C# 14 traz `field`, extension members e melhorias que a skill principal usa com parcimônia.
Recursos novos do .NET 10 relevantes: validação nativa em Minimal APIs, melhorias de OpenAPI, `Guid.CreateVersion7` (desde .NET 9).

## Trade-offs
- Material de terceiros ainda majoritariamente .NET 8/9 (irrelevante para as APIs usadas).
- Imagens `mcr.microsoft.com/dotnet/aspnet:10.0` disponíveis; ECS/Fargate agnósticos.

## Consequências
- Não adotar previews (.NET 11) durante o projeto.
- Atualizações de patch automáticas via `rollForward`; upgrades de minor de pacotes revisados em `Directory.Packages.props`.
