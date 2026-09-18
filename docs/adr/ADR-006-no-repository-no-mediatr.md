# ADR-006 — DbContext direto via interface; sem repository/UoW genérico; sem MediatR

**Status**: aceita · **Data**: 2026-09-18

## Contexto
É comum em projetos .NET "de tutorial" empilhar `IRepository<T>`, `IUnitOfWork`, MediatR, AutoMapper e Result em todo lugar.
Queremos código explícito, testável e idiomático, sem abstrações cerimoniais — mas mantendo o domínio livre de infraestrutura.

## Opções
1. Repository genérico + UoW sobre o EF Core.
2. Repository por agregado (DDD tático) com implementação EF.
3. **`IFulfillmentHubDbContext`** (interface com `DbSet<T>` + `SaveChangesAsync`) usada diretamente pelos casos de uso; `Application` referencia o pacote EF Core.
4. Casos de uso dentro de `Infrastructure` (sem camada Application).

## Decisão
Opção 3 para dados. Casos de uso são classes explícitas (`PlaceOrderHandler`) registradas no DI e chamadas diretamente pelos
endpoints — **sem MediatR/Mediator**. Interfaces existem apenas em portas externas (`IDeliveryProviderClient`,
`IPaymentGatewayClient`, `IMessagePublisher`) e no DbContext. Um `Result` pequeno próprio para falhas esperadas; sem AutoMapper.

## Motivo
- `DbContext` **já é** Unit of Work + Repository; envolvê-lo duplica API, esconde LINQ/Include/projeções e cria "repository anêmico".
- MediatR virou licença comercial (2025) e, mais importante, não resolve problema real aqui: DI direto é mais rastreável.
- Testes de casos de uso rodam contra Postgres real (Testcontainers), o que testa mais do que mocks de repository.

## Trade-offs
- `Application` depende do pacote EF Core (não do provider). Aceito e explícito; `Domain` continua puro.
- Sem "pipeline behaviors" do MediatR: cross-cutting (logging, validação, transação) vai em endpoint filters, interceptors EF e decorators só se necessário.

## Consequências
- ArchitectureTests: `Domain` não referencia EF; `Application` não referencia `Infrastructure`.
- Skills copiadas que sugerem repository/MediatR são explicitamente sobrepostas pela skill principal (seção 12).
