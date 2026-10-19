namespace Zapper.Domain.Entities;

public sealed class StoredCard
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string AcmeToken { get; init; } = string.Empty;
}