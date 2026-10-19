namespace Zapper.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; init; }
    public string EmailAddress { get; init; } = string.Empty;
    public ICollection<StoredCard> StoredCards { get; init; } = new List<StoredCard>();
}