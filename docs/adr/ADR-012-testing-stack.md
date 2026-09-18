# ADR-012 — Stack de testes: xUnit v3, Shouldly, Testcontainers, WebApplicationFactory, NetArchTest

**Status**: aceita · **Data**: 2026-09-18

## Contexto
Os testes devem provar comportamento (idempotência, concorrência, outbox, retry, webhooks) com banco e fila reais, e garantir a arquitetura.

## Opções
- Framework: **xUnit v3** vs. NUnit vs. MSTest/TUnit.
- Asserções: FluentAssertions (v8 com licença comercial fora de OSS/uso pessoal) vs. **Shouldly** (BSD) vs. AwesomeAssertions (fork) vs. `Assert` puro.
- Banco em testes: EF InMemory / SQLite vs. **Testcontainers PostgreSQL**.
- Dublês: Moq (incidente SponsorLink em 2023) vs. NSubstitute vs. **fakes manuais** (+ NSubstitute se necessário).
- Arquitetura: **NetArchTest.Rules** vs. ArchUnitNET.
- Tempo: **`FakeTimeProvider`** (Microsoft.Extensions.TimeProvider.Testing).

## Decisão
xUnit v3 + Shouldly + Testcontainers (PostgreSQL, LocalStack) + `WebApplicationFactory` + fakes manuais nas portas + `FakeTimeProvider` + NetArchTest.Rules.
Provider in-memory do EF é proibido nos testes de integração.

## Motivo
Padrões amplamente reconhecidos, licenças permissivas, e o que testa de verdade: constraints, transações, SQL gerado, pipeline HTTP completo.

## Trade-offs
- Testes de integração exigem Docker (local e CI) e são mais lentos (mitigado: containers por coleção, banco limpo por teste com Respawn).
- Shouldly é menos conhecido que FluentAssertions (aceito pela licença).

## Consequências
- TEST_STRATEGY.md define a matriz obrigatória e as regras de qualidade; cada garantia terá link para o teste no README final.
