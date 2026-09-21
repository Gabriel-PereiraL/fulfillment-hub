# ADR-009 — OpenTelemetry with the built-in logging; Aspire Dashboard locally; CloudWatch/X-Ray on AWS

**Status**: accepted · **Date**: 2026-09-18

## Context
Real observability (structured logging, metrics, traces, alerts) is a requirement. We want portability (OTel) and few dependencies.

## Options
- Logging: Serilog vs. **`Microsoft.Extensions.Logging` + `LoggerMessage` + OTLP exporter**.
- Local backend: Grafana LGTM (Tempo/Prometheus/Loki), Jaeger + Prometheus, **Aspire Dashboard**.
- AWS: CloudWatch/X-Ray via the **ADOT collector**, or Datadog/Grafana Cloud (cost/external account).

## Decision
Built-in logging with `LoggerMessage` (JSON on the console + OTLP); traces/metrics with OpenTelemetry (ASP.NET Core, HttpClient, Npgsql,
runtime, AWS SDK, our own `ActivitySource`/`Meter`); Aspire Dashboard as the local backend; ADOT sidecar → CloudWatch (logs/metrics/alarms) and X-Ray (traces) on AWS.

## Rationale
No extra adapters; `trace_id` correlation in the logs is automatic; the Aspire Dashboard is a container with no configuration; CloudWatch is the requested target and cheap enough in dev.

## Trade-offs
- Without Serilog we lose the sinks/enrichers ecosystem (not needed here).
- The Aspire Dashboard does not persist data (fine for dev); Grafana LGTM stays as the documented alternative for persistent dashboards.
- X-Ray via OTel has attribute limitations; acceptable.

## Consequences
- Catalog of metrics/spans/alerts in OBSERVABILITY.md; runbook executed in Phase 11.
- `traceparent` propagated through the outbox and SQS.

## Addendum 2026-09-21 — local backend changed to `grafana/otel-lgtm` (D-78)
The hardening track needed alert rules evaluated against persisted metrics, which the Aspire Dashboard cannot do.
The local backend is now `grafana/otel-lgtm` (OTel Collector + Prometheus + Tempo + Loki + Grafana, one container,
pinned to 0.33.1), with the FulfillmentHub dashboard and five alert rules provisioned from files under `observability/`.
The application code and the OTLP export did not change; the Aspire Dashboard remains an opt-in compose profile.
The AWS decision (ADOT → CloudWatch/X-Ray) is unchanged; the local rules are the template for the CloudWatch alarms.
