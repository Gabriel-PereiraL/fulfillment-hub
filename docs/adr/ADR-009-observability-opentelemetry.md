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
