using FulfillmentHub.Api.Admin;
using FulfillmentHub.Api.Catalog;
using FulfillmentHub.Api.ErrorHandling;
using FulfillmentHub.Api.Identity;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.Api.Orders;
using FulfillmentHub.Api.Webhooks;
using FulfillmentHub.Application;
using FulfillmentHub.Infrastructure.Identity;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.Infrastructure.Seeding;
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
builder.Services.AddFulfillmentHubIdentity();
builder.Services.AddFulfillmentHubPaymentProvider();
builder.Services.AddFulfillmentHubDeliveryProvider();
builder.Services.AddFulfillmentHubApplication();
builder.Services.AddFulfillmentHubIdentityApplication();
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddScoped<WebhookReceiver>();

builder.Services.AddFulfillmentHubApiSecurity();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<FulfillmentHubDbContext>("postgres", tags: ["ready"]);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddFulfillmentHubDevelopmentSeeding();
}

var app = builder.Build();

// `dotnet run -- seed`: writes fictional development data and exits. Never part of the normal startup path.
if (args is ["seed"])
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Seeding is only available in the Development environment.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapProductsEndpoints();
app.MapOrdersEndpoints();
app.MapPaymentWebhooksEndpoints();
app.MapDeliveryWebhooksEndpoints();
app.MapOutboxAdminEndpoints();

app.Run();

/// <summary>Exposed for integration tests (<c>WebApplicationFactory&lt;Program&gt;</c>).</summary>
public partial class Program;
