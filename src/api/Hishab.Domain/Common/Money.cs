using System.Globalization;

namespace Hishab.Domain.Common;

/// <summary>
/// An amount in Bangladeshi taka (BDT, the only currency Hishab handles). Backed by
/// <see cref="decimal"/>, never <see cref="double"/>: binary floating point cannot hold 0.1
/// exactly, so sums of prices drift. At most 2 decimal places (poisha); more is rejected
/// rather than silently rounded, because a rounded amount is a wrong amount.
/// May be negative (refunds, balances); rules such as "charges are non-negative" belong to
/// the entity that uses it.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public const string Currency = "BDT";
    public const int MaxDecimals = 2;

    private Money(decimal amount) => Amount = amount;

    public decimal Amount { get; }

    public static Money Zero { get; } = new(0m);

    public bool IsNegative => Amount < 0;

    /// <exception cref="DomainValidationException">More than 2 decimal places.</exception>
    public static Money Bdt(decimal amount, string field = "amount") =>
        HasValidScale(amount)
            ? new Money(amount)
            : throw new DomainValidationException(field, $"Amounts can have at most {MaxDecimals} decimal places.");

    /// <summary>True when the amount has no more than 2 significant decimal places (12.50 is fine, 12.505 is not).</summary>
    public static bool HasValidScale(decimal amount) => decimal.Round(amount, MaxDecimals) == amount;

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static Money Add(Money left, Money right) => left + right;

    public static Money Subtract(Money left, Money right) => left - right;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");
}
