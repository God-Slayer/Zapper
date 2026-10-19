using Zapper.Domain.Entities;

namespace Zapper.Application.Abstractions;

public interface ISubMerchantRepository
{
    Task<SubMerchant?> GetByZapperMerchantIdAsync(string zapperMerchantId, CancellationToken cancellationToken);
}