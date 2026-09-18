# ADR-001 — Monólito modular com três processos

**Status**: aceita · **Data**: 2026-09-18

## Contexto
Um desenvolvedor, projeto de portfólio C#/.NET, produto com módulos claros (Catalog, Customers, Orders, Payments,
Deliveries, Identity, Operations), integrações externas simuladas, necessidade de processamento assíncrono.
O objetivo é demonstrar engenharia defensável, não "arquitetura de LinkedIn".

## Opções consideradas
1. **Monólito modular** (uma solução, um banco, módulos por pasta, processos separados só onde o ciclo de vida difere).
2. Microserviços (um serviço por módulo, bancos separados, comunicação via HTTP/fila).
3. Monólito "clássico" em um único processo (API + workers no mesmo host).

## Decisão
Opção 1: monólito modular com **três processos**: `Api` (HTTP), `Worker` (outbox, consumidores, reconciliação) e
`ProviderSimulator` (sistemas externos simulados). `Admin` (Blazor) será um quarto host na Fase 17.
Camadas em projetos (`Domain`, `Application`, `Infrastructure`) + pastas por módulo + `ArchitectureTests`.

## Motivo
- Microserviços multiplicam custo operacional (deploy, rede, observabilidade distribuída, consistência) sem ganho para 1 dev.
- Separar API e Worker é justificado: perfis de escala e reinício diferentes, e é o que se faz no ECS (dois serviços).
- O Simulator precisa ser processo separado para que a integração HTTP seja real (rede, timeouts, falhas).

## Trade-offs
- Um único banco = acoplamento de schema entre módulos (aceito; comunicação entre módulos por eventos/outbox onde faz sentido).
- Sem isolamento de falhas por módulo (aceito; resiliência é por integração, não por serviço).

## Consequências
- Testes de arquitetura garantem a direção das dependências.
- Se um módulo precisar escalar isoladamente no futuro, a extração é possível porque as fronteiras já existem.
