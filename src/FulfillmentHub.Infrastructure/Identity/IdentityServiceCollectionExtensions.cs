using FulfillmentHub.Application.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>Password hashing, token issuing and the login use case. Token validation is configured by the host.</summary>
    public static IServiceCollection AddFulfillmentHubIdentity(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenService>();
        services.AddScoped<LoginHandler>();
        services.AddScoped<UserQueries>();

        return services;
    }
}
