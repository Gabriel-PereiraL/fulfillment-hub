using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.IntegrationTests.Persistence;

[Collection(ApiTests.Name)]
public sealed class MigrationsTests(ApiFixture api)
{
    [Fact]
    public async Task Migrations_AreFullyAppliedOnCleanDatabase()
    {
        using var scope = api.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

        var pending = await dbContext.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        var applied = await dbContext.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        pending.ShouldBeEmpty();
        applied.ShouldNotBeEmpty();
    }

    [Fact]
    public void Model_HasNoChangesMissingFromMigrations()
    {
        using var scope = api.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

        // Fails when an entity/configuration was changed without `dotnet ef migrations add`.
        dbContext.Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
