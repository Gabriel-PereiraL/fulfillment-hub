# Documentation — FulfillmentHub

Index of the technical documentation. Start with **PROJECT_STATE.md** (current state) and **PRODUCT.md** (what the system is).

| Document | Contents |
|---|---|
| [PROJECT_STATE.md](PROJECT_STATE.md) | **Operational memory**: current phase, what is done, next step, blockers, gate. Updated at the end of every session |
| [PRODUCT.md](PRODUCT.md) | What it is and what it is for, actors, high-level flow, features by module, out of scope, non-functional requirements, glossary |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Modular monolith, projects, dependency rules, patterns adopted and rejected, cross-cutting components, critical flows |
| [DOMAIN.md](DOMAIN.md) | Modules, aggregates, entities, value objects, state machines, invariants, infrastructure records |
| [INTEGRATIONS.md](INTEGRATIONS.md) | Real Uber Direct contract (sources/dates) vs. the simulator; simulated payment provider; resilience policy; webhook ingestion; queue vs. synchronous |
| [ROADMAP.md](ROADMAP.md) | 21 phases with objective, tasks, acceptance criteria (gates), dependencies, risks and status |
| [BACKLOG.md](BACKLOG.md) | Items by category with priority P0–P3 and status |
| [TEST_STRATEGY.md](TEST_STRATEGY.md) | Test stack, pyramid, mandatory scenario matrix (T1–T20), quality rules |
| [SECURITY.md](SECURITY.md) | Threat model, authentication/authorization, secrets, OWASP Top 10 checklist, personal data, SAST |
| [OBSERVABILITY.md](OBSERVABILITY.md) | Logs, metrics, traces, correlation, alerts, runbooks |
| [AWS_ARCHITECTURE.md](AWS_ARCHITECTURE.md) | Target AWS architecture, cost, networking, IAM, what operating it is meant to teach |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Environments, Docker, CI, Terraform, deploy, rollback, leak-prevention checklist |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Prerequisites, setup, everyday commands, configuration and secrets, workflow |
| [DECISIONS.md](DECISIONS.md) | Decision log (ADR index, smaller decisions, pending decisions) |
| [adr/](adr/) | ADR-001 … ADR-013 |

## Project invariants

1. C#/.NET is the stack. 2. The backend is the focus. 3. No microservices for their own sake. 4. No Kubernetes. 5. No complexity without a need.
6. External integrations are simulated. 7. That is always stated explicitly. 8. No real or commercial calls without authorization. 9. No secrets in code.
10. No push without explicit authorization. 11. Published on GitHub since Phase 9, with the full history preserved. 12. AI skills and memory never enter the public repository.
13. Documentation before implementation. 14. PROJECT_STATE is always up to date. 15. There is always a clear next step.
16. Tests validate behaviour. 17. Security and observability are requirements, not afterthoughts. 18. AWS is learned by operating it.
19. The project stays runnable while it evolves. 20. The portfolio never lies.

## Notice

This project does not connect to Uber infrastructure or to any real payment provider. The simulators reproduce a limited subset of
public API contracts for educational and portfolio purposes only.
