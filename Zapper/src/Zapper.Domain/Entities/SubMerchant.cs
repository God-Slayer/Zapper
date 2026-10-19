namespace Zapper.Domain.Entities;

public sealed class SubMerchant
{
    public Guid Id { get; init; }
    public string ZapperMerchantId { get; init; } = string.Empty;
    public string TradingName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string RegionCode { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public string MerchantCategoryCode { get; init; } = string.Empty;
}