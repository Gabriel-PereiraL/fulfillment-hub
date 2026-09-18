using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Application.Common;

public sealed record MoneyDto(decimal Amount, string Currency)
{
    public static MoneyDto From(Money money) => new(money.Amount, money.Currency);

    public static MoneyDto? FromOptional(Money? money) => money is null ? null : From(money);
}
