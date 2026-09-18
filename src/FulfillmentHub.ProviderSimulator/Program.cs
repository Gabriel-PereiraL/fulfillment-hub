using System.Text.Json;
using FulfillmentHub.ProviderSimulator.Common;
using FulfillmentHub.ProviderSimulator.Payments;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FulfillmentHub.ProviderSimulator;

/// <summary>
/// Entry point. A named class (instead of top-level statements) so tests can host the simulator with
/// <c>WebApplicationFactory&lt;ProviderSimulator.Program&gt;</c> without clashing with the API's own <c>Program</c>.
/// </summary>
public sealed class Program
{
    private Program()
    {
    }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("fulfillmenthub-provider-simulator"))
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithLogging();

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        // Real providers speak snake_case JSON; so does the simulator.
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        });

        builder.Services.AddOptions<ChaosOptions>().BindConfiguration(ChaosOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddOptions<PaymentSimulatorOptions>().BindConfiguration(PaymentSimulatorOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpClient(WebhookDispatcher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddSingleton<WebhookOutbox>();
        builder.Services.AddHostedService<WebhookDispatcher>();
        builder.Services.AddSingleton<PaymentSimulatorStore>();
        builder.Services.AddHostedService<PaymentSettlementService>();

        builder.Services.AddValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapPaymentSimulatorEndpoints();

        app.Run();
    }
}
