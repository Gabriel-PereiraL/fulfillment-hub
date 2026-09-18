using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Application.Common.Persistence;

/// <summary>
/// The persistence seam used by application use cases (ADR-006): aggregate roots as <see cref="DbSet{TEntity}"/>
/// plus <see cref="SaveChangesAsync"/>. There is intentionally no repository or unit-of-work layer on top of EF Core.
/// </summary>
public interface IFulfillmentHubDbContext
{
    DbSet<Product> Products { get; }

    DbSet<Customer> Customers { get; }

    DbSet<Order> Orders { get; }

    DbSet<Payment> Payments { get; }

    DbSet<DeliveryQuote> DeliveryQuotes { get; }

    DbSet<Delivery> Deliveries { get; }

    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
