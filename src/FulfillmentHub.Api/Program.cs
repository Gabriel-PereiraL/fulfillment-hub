using FulfillmentHub.Api.Admin;
using FulfillmentHub.Api.Catalog;
using FulfillmentHub.Api.ErrorHandling;
using FulfillmentHub.Api.Identity;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.Api.Orders;
using FulfillmentHub.Api.Webhooks;
using FulfillmentHub.Application;
using FulfillmentHub.Infrastructure.Identity;
using FulfillmentHub.Infrastructure.Messaging;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.Infrastructure.Seeding;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Request body ceiling for the whole API (D-83). Kestrel does not bind `Limits` from configuration on its own, so the
// value in appsettings (`Kestrel:Limits:MaxRequestBodySize`, 256 KB) is applied here; webhooks keep their own 64 KB.
builder.WebHost.ConfigureKestrel(kestrel =>
    kestrel.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("Kestrel:Limits:MaxRequestBodySize") ?? 262_144);

builder.AddFulfillmentHubTelemetry("fulfillmenthub-api")
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

builder.Services.AddFulfillmentHubPersistence();
builder.Services.AddFulfillmentHubIdentity();
builder.Services.AddFulfillmentHubPaymentProvider();
builder.Services.AddFulfillmentHubDeliveryProvider();
builder.Services.AddFulfillmentHubMessaging();
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

// `FulfillmentHub.Api.dll migrate`: applies pending EF Core migrations and exits — the explicit one-off task that the
// compose "app" profile (and, later, an ECS run-task) executes before the hosts start. Never part of the normal
// startup path (SECURITY.md T14: no automatic Migrate() when the API boots).
if (args is ["migrate"])
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
    var pending = string.Join(", ", await db.Database.GetPendingMigrationsAsync());
    await db.Database.MigrateAsync();
    StartupCommands.LogMigrated(app.Logger, pending.Length == 0 ? "(none)" : pending);
    return;
}

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

// Security headers first so every response — including 401/404 produced by the pipeline itself — carries them (D-82).
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    // Only effective on requests seen as HTTPS; behind the load balancer that needs ForwardedHeaders (BL-113, Phase 16).
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
// After authentication so the order-creation limiter can partition by user (D-84); before authorization so
// rejected requests are shed before policies run.
app.UseRateLimiter();
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

/// <summary>Log messages of the one-off commands (`migrate`, `seed`) handled before the host starts.</summary>
internal static partial class StartupCommands
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Database migrated; applied: {Migrations}")]
    public static partial void LogMigrated(ILogger logger, string migrations);
}
