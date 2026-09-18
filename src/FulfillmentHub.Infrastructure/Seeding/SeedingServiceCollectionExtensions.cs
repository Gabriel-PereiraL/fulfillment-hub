using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.Infrastructure.Seeding;

public static class SeedingServiceCollectionExtensions
{
    /// <summary>Registers the development seeder. Options are validated when resolved (i.e. only when seeding runs).</summary>
    public static IServiceCollection AddFulfillmentHubDevelopmentSeeding(this IServiceCollection services)
    {
        services.AddOptions<SeedOptions>()
            .BindConfiguration(SeedOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddScoped<DevelopmentSeeder>();

        return services;
    }
}
