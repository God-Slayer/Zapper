using Microsoft.EntityFrameworkCore;
using Zapper.Application.Abstractions;
using Zapper.Domain.Entities;
using Zapper.Infrastructure.Persistence;

namespace Zapper.Infrastructure.Repositories;

public sealed class CustomerRepository(ZapperDbContext dbContext) : ICustomerRepository
{
    public Task<Customer?> GetWithStoredCardsAsync(Guid customerId, CancellationToken cancellationToken) =>
        dbContext.Customers.Include(x => x.StoredCards).SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
}