using FulfillmentHub.Api.ErrorHandling;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddFulfillmentHubTelemetry("fulfillmenthub-api")
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddFulfillmentHubPersistence();
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<FulfillmentHubDbContext>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

/// <summary>Exposed for integration tests (<c>WebApplicationFactory&lt;Program&gt;</c>).</summary>
public partial class Program;
