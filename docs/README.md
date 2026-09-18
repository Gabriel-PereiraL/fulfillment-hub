# Documentation — FulfillmentHub

Index of the technical documentation. Start with **PROJECT_STATE.md** (current status) and **PRODUCT.md** (what the system is).

| Document | Contents |
|---|---|
| [PROJECT_STATE.md](PROJECT_STATE.md) | Project status: completed phases, current milestone, implemented capabilities, what is not implemented yet, next phase |
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
| [DEPLOYMENT.md](DEPLOYMENT.md) | Environments, Docker, CI, Terraform, deploy, rollback, pre-publication checklist |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Prerequisites, setup, everyday commands, configuration and secrets, workflow |
| [DECISIONS.md](DECISIONS.md) | Decision log (ADR index, smaller decisions, pending decisions) |
| [adr/](adr/) | ADR-001 … ADR-013 |

## Project principles

1. C#/.NET is the stack; the backend is the focus. 2. Modular monolith: no microservices, Kubernetes or Kafka for their own sake; no complexity without a need.
3. External integrations are simulated, and that is always stated explicitly; no real or commercial calls. 4. No secrets in code.
5. Documentation before implementation; the status, roadmap and backlog are kept up to date. 6. Tests validate behaviour.
7. Security and observability are requirements, not afterthoughts. 8. AWS is learned by operating it, with cost kept in check.
9. The project stays runnable while it evolves (green build and tests at every step). 10. The portfolio never lies: nothing is claimed as done that is not.

## Notice

This project does not connect to Uber infrastructure or to any real payment provider. The simulators reproduce a limited subset of
public API contracts for educational and portfolio purposes only.
