using System.Diagnostics.CodeAnalysis;

namespace FulfillmentHub.Application.Common;

/// <summary>
/// Outcome of a use case: a value or a single expected <see cref="Failure"/>. Deliberately small — no monads,
/// no hierarchies (ADR-006). Domain invariant violations still throw <c>DomainException</c>.
/// </summary>
public sealed class Result<T>
{
    internal Result(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool IsSuccess => Failure is null;

    public T? Value { get; }

    public Failure? Failure { get; }

    public static implicit operator Result<T>(Failure failure) => Result.Fail<T>(failure);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Failure, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value) : onFailure(Failure);
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new(value, null);

    public static Result<T> Fail<T>(Failure failure) => new(default, failure);
}
