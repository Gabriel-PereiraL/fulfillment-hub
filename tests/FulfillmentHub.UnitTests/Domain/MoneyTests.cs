using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class MoneyTests
{
    [Fact]
    public void Of_RoundsToTwoDecimalsAndNormalizesCurrency()
    {
        var money = Money.Of(10.005m, "brl");

        money.Amount.ShouldBe(10.00m); // banker's rounding: 10.005 -> 10.00
        money.Currency.ShouldBe("BRL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("BR")]
    [InlineData("REAL")]
    public void Of_WithInvalidCurrency_ThrowsDomainException(string currency)
    {
        var act = () => Money.Of(1m, currency);

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void Add_WithSameCurrency_SumsAmounts()
    {
        var total = Money.Of(19.90m) + Money.Of(5.10m);

        total.ShouldBe(Money.Of(25.00m));
    }

    [Fact]
    public void Add_WithDifferentCurrencies_ThrowsDomainException()
    {
        var act = () => Money.Of(1m, "BRL") + Money.Of(1m, "USD");

        act.ShouldThrow<DomainException>().Message.ShouldContain("BRL");
    }

    [Fact]
    public void Multiply_ByQuantity_ScalesAmount()
    {
        var lineTotal = Money.Of(3.33m) * 3;

        lineTotal.ShouldBe(Money.Of(9.99m));
    }

    [Fact]
    public void Subtract_BelowZero_IsAllowedAndFlaggedAsNegative()
    {
        var balance = Money.Of(5m) - Money.Of(7.5m);

        balance.Amount.ShouldBe(-2.5m);
        balance.IsNegative.ShouldBeTrue();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        Money.Of(1.10m).ShouldBe(Money.Of(1.1m));
        Money.Of(1m, "BRL").ShouldNotBe(Money.Of(1m, "USD"));
    }
}
