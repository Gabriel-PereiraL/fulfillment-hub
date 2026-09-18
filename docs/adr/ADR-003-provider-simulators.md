# ADR-003 — Providers externos simulados

**Status**: aceita · **Data**: 2026-09-18

## Contexto
O produto integra com um provedor de entregas e um de pagamentos. Não há (nem deve haver) credenciais, entregas ou
cobranças reais. Precisamos exercitar resiliência, idempotência e webhooks em cenários de falha controlados.

## Opções
1. Chamar sandbox real (Uber Direct test mode) — exige conta, credenciais, termos; não controla falhas; risco de confundir portfólio com integração comercial.
2. Mocks só em testes (sem processo real) — não exercita rede, timeouts, webhooks reais.
3. **Simulator próprio em .NET** reproduzindo um subconjunto documentado do contrato público, com cenários configuráveis.

## Decisão
Opção 3. `FulfillmentHub.ProviderSimulator` hospeda `/delivery/v1` (subconjunto da Uber Direct API: token, quote, create, get,
cancel, erros, webhook `event.delivery_status` assinado) e `/payments/v1` (contrato próprio minimalista). Cenários via variáveis
(`SIM_*`). Documentação obrigatória do contrato real vs. simulado em `INTEGRATIONS.md`, com fontes e datas.

## Motivo
Controle total de falhas (latência, 429, 5xx, timeout, webhooks duplicados/fora de ordem), reprodutibilidade em testes e CI,
zero risco legal/financeiro, e mais código .NET para demonstrar (o simulator também é backend).

## Trade-offs
- O simulator pode divergir do provider real; mitigação: tabela "REAL vs SIMULATOR" e contract tests contra a spec OpenAPI oficial.
- Não prova integração comercial — e o projeto **nunca afirmará** isso.

## Consequências
- Disclaimer fixo em README, docs e no próprio simulator: "This project does not connect to Uber infrastructure…".
- `IDeliveryProviderClient` é a única porta; um segundo provider (BL-069) é possível sem tocar no domínio.
