using Zapper.Domain.Entities;

namespace Zapper.Application.Abstractions;

public interface ICustomerRepository
{
    Task<Customer?> GetWithStoredCardsAsync(Guid customerId, CancellationToken cancellationToken);
}