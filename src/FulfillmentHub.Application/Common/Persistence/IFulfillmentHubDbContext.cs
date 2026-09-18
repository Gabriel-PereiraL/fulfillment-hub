namespace FulfillmentHub.Application.Common.Persistence;

/// <summary>
/// The persistence seam used by application use cases (ADR-006). Aggregate <c>DbSet</c>s are added
/// here as the domain model grows; there is intentionally no repository or unit-of-work layer on top of EF Core.
/// </summary>
public interface IFulfillmentHubDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
