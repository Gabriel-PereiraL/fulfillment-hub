using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Seeding;

/// <summary>
/// Seeds fictional development data: one user per role, a customer profile and a few products. Idempotent (keyed by
/// e-mail / SKU). Passwords come from configuration (user-secrets), never from code, and are never logged.
/// Only ever invoked explicitly by the host (<c>dotnet run -- seed</c>) in Development.
/// </summary>
public sealed partial class DevelopmentSeeder(
    FulfillmentHubDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider,
    ILogger<DevelopmentSeeder> logger)
{
    public const string AdminEmail = "admin@fulfillmenthub.local";
    public const string OperatorEmail = "operator@fulfillmenthub.local";
    public const string CustomerEmail = "customer@fulfillmenthub.local";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        var now = timeProvider.GetUtcNow();

        await EnsureUserAsync(AdminEmail, seed.AdminPassword, [Role.Admin, Role.Operator], now, cancellationToken);
        await EnsureUserAsync(OperatorEmail, seed.OperatorPassword, [Role.Operator], now, cancellationToken);
        var customerUser = await EnsureUserAsync(CustomerEmail, seed.CustomerPassword, [Role.Customer], now, cancellationToken);

        if (!await db.Customers.AnyAsync(c => c.UserId == customerUser.Id, cancellationToken))
        {
            var customer = Customer.Register(customerUser.Id, "Test Customer", customerUser.Email, PhoneNumber.Of("+5581999990000"), now);
            customer.AddAddress("Home", Address.Create("Rua das Flores", "123", "Apto 4", "Boa Viagem", "Recife", "PE", "51020-000", latitude: -8.12, longitude: -34.9), now);
            db.Customers.Add(customer);
        }

        foreach (var (sku, name, price, stock) in Products)
        {
            if (!await db.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
            {
                db.Products.Add(Product.Create(sku, name, Money.Of(price), stock, now));
            }
        }

        var changes = await db.SaveChangesAsync(cancellationToken);
        LogSeeded(changes);
    }

    private static readonly (string Sku, string Name, decimal Price, int Stock)[] Products =
    [
        ("BOOK-CLEAN-CODE", "Book: Clean Code", 89.9m, 25),
        ("MUG-DOTNET", ".NET mug", 39.5m, 100),
        ("TSHIRT-FH-M", "FulfillmentHub t-shirt (M)", 59m, 40),
        ("STICKER-PACK", "Sticker pack", 12m, 500),
        ("LAST-UNIT", "Single-unit item (concurrency tests)", 199m, 1),
    ];

    private async Task<User> EnsureUserAsync(string email, string password, Role[] roles, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var address = EmailAddress.Of(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == address, cancellationToken);

        if (user is not null)
        {
            return user;
        }

        user = User.Create(address, passwordHasher.Hash(password), roles, now);
        db.Users.Add(user);
        return user;
    }

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information, Message = "Development seed completed ({Changes} rows written)")]
    private partial void LogSeeded(int changes);
}
