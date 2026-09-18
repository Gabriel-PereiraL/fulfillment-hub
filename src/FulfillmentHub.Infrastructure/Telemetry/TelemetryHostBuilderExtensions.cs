using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FulfillmentHub.Infrastructure.Telemetry;

public static class TelemetryHostBuilderExtensions
{
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Configures the telemetry shared by every FulfillmentHub host: structured logs (with scopes and
    /// trace correlation), traces and metrics via OpenTelemetry, and OTLP export when an endpoint is configured.
    /// Hosts add their own instrumentation (e.g. ASP.NET Core) on the returned builder.
    /// </summary>
    public static IOpenTelemetryBuilder AddFulfillmentHubTelemetry(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId;
        });

        if (!builder.Environment.IsDevelopment())
        {
            // Containers and log collectors ingest one JSON object per line.
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "O";
            });
        }

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: typeof(TelemetryHostBuilderExtensions).Assembly
                    .GetName().Version?.ToString() ?? "unknown")
                .AddAttributes([new KeyValuePair<string, object>(
                    "deployment.environment.name", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                .AddSource(TelemetryNames.ActivitySource)
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TelemetryNames.Meter)
                .AddMeter("Npgsql"))
            .WithLogging(
                configureBuilder: null,
                configureOptions: options =>
                {
                    options.IncludeScopes = true;
                    options.IncludeFormattedMessage = true;
                });

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            telemetry.UseOtlpExporter();
        }

        return telemetry;
    }
}
