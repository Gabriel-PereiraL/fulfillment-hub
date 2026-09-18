using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Idempotency;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Infrastructure.Persistence;

public sealed class FulfillmentHubDbContext(DbContextOptions<FulfillmentHubDbContext> options)
    : DbContext(options), IFulfillmentHubDbContext
{
    public const string OrderNumberSequence = "order_number_seq";

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<DeliveryQuote> DeliveryQuotes => Set<DeliveryQuote>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<User> Users => Set<User>();

    /// <summary>Infrastructure record (ADR-010); not part of <see cref="IFulfillmentHubDbContext"/>.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Infrastructure record (webhook inbox); not part of <see cref="IFulfillmentHubDbContext"/>.</summary>
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ProductId>().HaveConversion<StronglyTypedIdConverter<ProductId>>();
        configurationBuilder.Properties<CustomerId>().HaveConversion<StronglyTypedIdConverter<CustomerId>>();
        configurationBuilder.Properties<OrderId>().HaveConversion<StronglyTypedIdConverter<OrderId>>();
        configurationBuilder.Properties<PaymentId>().HaveConversion<StronglyTypedIdConverter<PaymentId>>();
        configurationBuilder.Properties<DeliveryId>().HaveConversion<StronglyTypedIdConverter<DeliveryId>>();
        configurationBuilder.Properties<DeliveryQuoteId>().HaveConversion<StronglyTypedIdConverter<DeliveryQuoteId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<StronglyTypedIdConverter<UserId>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(OrderNumberSequence).StartsAt(1000);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FulfillmentHubDbContext).Assembly);
    }
}
