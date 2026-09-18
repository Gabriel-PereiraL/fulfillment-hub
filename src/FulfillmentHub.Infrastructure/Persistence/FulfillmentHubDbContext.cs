using FulfillmentHub.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Infrastructure.Persistence;

public sealed class FulfillmentHubDbContext(DbContextOptions<FulfillmentHubDbContext> options)
    : DbContext(options), IFulfillmentHubDbContext
{
    // Entity configurations (IEntityTypeConfiguration<T>) are applied here from Phase 2 onwards.
}
