using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class ProductTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Create_NormalizesSkuAndStartsActive()
    {
        var product = Product.Create("  abc-123 ", " Widget ", Money.Of(9.99m), 3, Now);

        product.Sku.ShouldBe("ABC-123");
        product.Name.ShouldBe("Widget");
        product.IsActive.ShouldBeTrue();
        product.StockQuantity.ShouldBe(3);
    }

    [Theory]
    [InlineData("", "Name", 1, 0)]
    [InlineData("SKU", "", 1, 0)]
    [InlineData("SKU", "Name", 0, 0)]
    [InlineData("SKU", "Name", 1, -1)]
    public void Create_WithInvalidData_Throws(string sku, string name, decimal price, int stock)
    {
        var act = () => Product.Create(sku, name, Money.Of(price), stock, Now);

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void Reserve_DecrementsStock()
    {
        var product = TestData.Product(stock: 5);

        product.Reserve(3, Now);

        product.StockQuantity.ShouldBe(2);
    }

    [Fact]
    public void Reserve_MoreThanAvailable_ThrowsAndKeepsStock()
    {
        var product = TestData.Product(stock: 2);

        var act = () => product.Reserve(3, Now);

        var exception = act.ShouldThrow<InsufficientStockException>();
        exception.Requested.ShouldBe(3);
        exception.Available.ShouldBe(2);
        product.StockQuantity.ShouldBe(2);
    }

    [Fact]
    public void Reserve_ExactlyAvailable_LeavesZero_NeverNegative()
    {
        var product = TestData.Product(stock: 2);

        product.Reserve(2, Now);

        product.StockQuantity.ShouldBe(0);
        Should.Throw<InsufficientStockException>(() => product.Reserve(1, Now));
    }

    [Fact]
    public void Reserve_OnInactiveProduct_Throws()
    {
        var product = TestData.Product(stock: 5);
        product.Deactivate(Now);

        Should.Throw<DomainException>(() => product.Reserve(1, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void ReserveAndRelease_WithNonPositiveQuantity_Throw(int quantity)
    {
        var product = TestData.Product(stock: 5);

        Should.Throw<DomainException>(() => product.Reserve(quantity, Now));
        Should.Throw<DomainException>(() => product.Release(quantity, Now));
    }

    [Fact]
    public void Release_ReturnsStock()
    {
        var product = TestData.Product(stock: 5);
        product.Reserve(4, Now);

        product.Release(4, Now.AddMinutes(1));

        product.StockQuantity.ShouldBe(5);
        product.UpdatedAt.ShouldBe(Now.AddMinutes(1));
    }
}
