using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Zapper.Domain.Entities;
using Zapper.Infrastructure.Persistence;
using Zapper.Infrastructure.Repositories;

namespace Zapper.Tests;

public sealed class PostgreSqlPaymentRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("zapper_tests")
        .WithUsername("zapper_tests")
        .WithPassword("zapper_tests")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task TryAddAsync_UsesPostgreSqlUniqueIndexToRejectDuplicateAppPaymentId()
    {
        await using var context = CreateContext();
        var repository = new PaymentRepository(context);
        var appPaymentId = Guid.NewGuid();

        var firstInserted = await repository.TryAddAsync(NewPayment(appPaymentId), CancellationToken.None);
        await context.SaveChangesAsync();
        var collisionInserted = await repository.TryAddAsync(NewPayment(appPaymentId), CancellationToken.None);

        Assert.True(firstInserted);
        Assert.False(collisionInserted);
        Assert.Equal(1, await context.Payments.CountAsync(x => x.AppPaymentId == appPaymentId));
    }

    private ZapperDbContext CreateContext() => new(new DbContextOptionsBuilder<ZapperDbContext>()
        .UseNpgsql(_postgres.GetConnectionString())
        .Options);

    private static Payment NewPayment(Guid appPaymentId) => new()
    {
        Id = Guid.NewGuid(),
        AppPaymentId = appPaymentId,
        CustomerId = Guid.NewGuid(),
        SubMerchantId = Guid.NewGuid(),
        AmountMinorUnits = 1000,
        CurrencyCode = "ZAR",
        AcmeTransactionId = Guid.NewGuid().ToString(),
        AcmeOrderNumber = Guid.NewGuid().ToString("N")[..20],
        RetrievalReferenceNumber = Guid.NewGuid().ToString("N")[..12],
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}