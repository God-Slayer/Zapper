namespace Zapper.Domain.ValueObjects;

/// <summary>
/// An amount in the currency's minor unit (for example, 1999 ZAR cents is R19.99).
/// </summary>
public sealed record PaymentAmount(long MinorUnits, string CurrencyCode);