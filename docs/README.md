# Documentação — FulfillmentHub

Índice da documentação técnica. Comece por **PROJECT_STATE.md** (estado atual) e **PRODUCT.md** (o que é o produto).

| Documento | Conteúdo |
|---|---|
| [PROJECT_STATE.md](PROJECT_STATE.md) | **Memória operacional**: fase atual, feito, próximo passo, bloqueios, gate. Atualizado a cada sessão |
| [PRODUCT.md](PRODUCT.md) | O que é, para que serve, atores, fluxo macro, funcionalidades, fora de escopo, requisitos não funcionais, glossário |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Monólito modular, projetos, dependências, padrões adotados/recusados, componentes transversais, fluxos críticos |
| [DOMAIN.md](DOMAIN.md) | Módulos, agregados, entidades, value objects, máquinas de estado, invariantes, registros de infraestrutura |
| [INTEGRATIONS.md](INTEGRATIONS.md) | Contrato real do Uber Direct (fontes/datas) vs. simulador; provider de pagamento simulado; resiliência; webhooks; fila vs. síncrono |
| [ROADMAP.md](ROADMAP.md) | 21 fases com objetivo, tasks, critérios de aceite (gates), dependências, riscos, status |
| [BACKLOG.md](BACKLOG.md) | Itens por categoria com prioridade P0–P3 e status |
| [TEST_STRATEGY.md](TEST_STRATEGY.md) | Stack de testes, pirâmide, matriz de cenários obrigatórios (T1–T20), regras de qualidade |
| [SECURITY.md](SECURITY.md) | Threat model, auth/authz, segredos, OWASP Top 10 checklist, LGPD, SAST |
| [OBSERVABILITY.md](OBSERVABILITY.md) | Logs, métricas, traces, correlação, alertas, runbooks |
| [AWS_ARCHITECTURE.md](AWS_ARCHITECTURE.md) | Arquitetura alvo na AWS, custos, rede, IAM, o que será aprendido operando |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Ambientes, Docker, CI, Terraform, deploy, rollback, checklist anti-vazamento |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Pré-requisitos, setup, comandos, configuração/segredos, fluxo de trabalho |
| [DECISIONS.md](DECISIONS.md) | Registro de decisões (índice de ADRs, decisões menores, pendentes) |
| [adr/](adr/) | ADR-001 … ADR-013 |

## Invariantes do projeto

1. C#/.NET é a stack principal. 2. Backend é o foco. 3. Sem microserviços por estética. 4. Sem Kubernetes. 5. Sem complexidade sem necessidade.
6. Integrações externas são simuladas. 7. Isso é sempre explícito. 8. Nenhuma chamada comercial/real sem autorização. 9. Nenhum secret em código.
10. Nenhum push sem autorização explícita. 11. Publicado no GitHub a partir da Fase 9 (histórico preservado). 12. Skills e memória da IA nunca entram no repositório público.
13. Documentação antes da implementação. 14. PROJECT_STATE sempre atualizado. 15. Todo dia há um próximo passo claro.
16. Testes validam comportamento. 17. Segurança e observabilidade são requisitos, não pós-projeto. 18. AWS é aprendida operando.
19. O projeto permanece executável durante a evolução. 20. Não mentir no portfólio.

## Aviso

This project does not connect to Uber infrastructure or to any real payment provider. The simulators reproduce a limited subset of
public API contracts for educational and portfolio purposes only.
