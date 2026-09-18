using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Infrastructure.Idempotency;
using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FulfillmentHubDbContext"/> (scoped) backed by PostgreSQL, configured from the
    /// <c>Database</c> section. Configuration is validated at startup so a misconfigured host fails fast.
    /// </summary>
    public static IServiceCollection AddFulfillmentHubPersistence(this IServiceCollection services)
    {
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<FulfillmentHubDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options.UseNpgsql(database.ConnectionString, npgsql =>
            {
                npgsql.CommandTimeout(database.CommandTimeoutSeconds);
                npgsql.MigrationsAssembly(typeof(FulfillmentHubDbContext).Assembly.GetName().Name);
            });

            // PostgreSQL convention: unquoted snake_case identifiers keep raw SQL and psql sessions readable.
            options.UseSnakeCaseNamingConvention();

            // ADR-004: domain events become outbox rows in the same SaveChanges as the aggregate change.
            options.AddInterceptors(serviceProvider.GetRequiredService<OutboxInterceptor>());
        });

        services.AddScoped<IFulfillmentHubDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<FulfillmentHubDbContext>());

        services.AddSingleton<OutboxEventSerializer>();
        services.AddSingleton<OutboxInterceptor>();
        services.AddSingleton<IdempotencyStore>();
        services.AddSingleton<WebhookInbox>();
        services.AddSingleton<WebhookSignatureVerifier>();
        services.AddSingleton<WebhookEventProcessor>();

        return services;
    }
}
