using System.Globalization;

namespace FulfillmentHub.Domain.Common;

/// <summary>
/// A monetary amount in a single ISO 4217 currency, always rounded to two decimal places.
/// Amounts in different currencies cannot be combined.
/// </summary>
public sealed record Money
{
    public const string DefaultCurrency = "BRL";

    public decimal Amount { get; }

    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, string currency = DefaultCurrency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new DomainException($"Currency must be a 3-letter ISO 4217 code, got '{currency}'.");
        }

        return new Money(
            decimal.Round(amount, 2, MidpointRounding.ToEven),
            currency.ToUpperInvariant());
    }

    public static Money Zero(string currency = DefaultCurrency) => Of(0m, currency);

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount - other.Amount, Currency);
    }

    public Money Multiply(int factor) => Of(Amount * factor, Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator *(Money money, int factor) => money.Multiply(factor);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }
}
