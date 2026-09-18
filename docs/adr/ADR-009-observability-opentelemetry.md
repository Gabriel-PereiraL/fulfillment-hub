# ADR-009 — OpenTelemetry com logging nativo; Aspire Dashboard local; CloudWatch/X-Ray na AWS

**Status**: aceita · **Data**: 2026-09-18

## Contexto
Observabilidade real (logs estruturados, métricas, traces, alertas) é requisito. Queremos portabilidade (OTel) e poucas dependências.

## Opções
- Logging: Serilog vs. **`Microsoft.Extensions.Logging` + `LoggerMessage` + exportador OTLP**.
- Backend local: Grafana LGTM (Tempo/Prometheus/Loki), Jaeger + Prometheus, **Aspire Dashboard**.
- AWS: CloudWatch/X-Ray via **ADOT collector**, ou Datadog/Grafana Cloud (custo/conta externa).

## Decisão
Logging nativo com `LoggerMessage` (JSON no console + OTLP); traces/métricas com OpenTelemetry (ASP.NET Core, HttpClient, Npgsql,
runtime, AWS SDK, `ActivitySource`/`Meter` próprios); Aspire Dashboard como backend local; ADOT sidecar → CloudWatch (logs/métricas/alarmes) e X-Ray (traces) na AWS.

## Motivo
Sem adaptadores extras; correlação `trace_id` nos logs é automática; Aspire Dashboard é um container sem configuração; CloudWatch é o alvo pedido e barato o suficiente em dev.

## Trade-offs
- Sem Serilog perde-se ecossistema de sinks/enrichers (não necessário aqui).
- Aspire Dashboard não persiste dados (ok para dev); Grafana LGTM fica como alternativa documentada para dashboards persistentes.
- X-Ray via OTel tem limitações de atributos; aceitável.

## Consequências
- Catálogo de métricas/spans/alertas em OBSERVABILITY.md; runbook executado na Fase 11.
- `traceparent` propagado por outbox e SQS.
