using Microsoft.EntityFrameworkCore;
using Zapper.Application.Abstractions;
using Zapper.Domain.Entities;
using Zapper.Infrastructure.Persistence;

namespace Zapper.Infrastructure.Repositories;

public sealed class SubMerchantRepository(ZapperDbContext dbContext) : ISubMerchantRepository
{
    public Task<SubMerchant?> GetByZapperMerchantIdAsync(string zapperMerchantId, CancellationToken cancellationToken) =>
        dbContext.SubMerchants.SingleOrDefaultAsync(x => x.ZapperMerchantId == zapperMerchantId, cancellationToken);
}