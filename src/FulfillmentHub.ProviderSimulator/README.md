# FulfillmentHub.ProviderSimulator

Simulated external providers used by FulfillmentHub for local development, tests and demos.

> **This project does not connect to Uber infrastructure or to any real payment provider.**
> **The simulator reproduces a limited subset of public API contracts for educational and portfolio purposes only.**
> No real deliveries, charges or credentials are involved.

Planned routes (see `docs/INTEGRATIONS.md`):

- `/delivery/v1/...` — subset of the Uber Direct API contract (quotes, deliveries, cancel, status webhooks), Phase 6.
- `/payments/v1/...` — minimal payment provider contract (create, get, refund, status webhooks), Phase 5.

Failure scenarios (latency, error rates, timeouts, 429 + `Retry-After`, duplicate / delayed / out-of-order webhooks)
are configured through `SIM_*` settings, documented in `docs/INTEGRATIONS.md`.

Current state (Phase 1): host skeleton with `/health/live` and OpenTelemetry export only.
