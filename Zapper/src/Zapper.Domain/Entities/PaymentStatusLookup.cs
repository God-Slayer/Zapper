using Zapper.Domain.Enums;

namespace Zapper.Domain.Entities;

public sealed class PaymentStatusLookup
{
    public PaymentStatus StatusCode { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}
