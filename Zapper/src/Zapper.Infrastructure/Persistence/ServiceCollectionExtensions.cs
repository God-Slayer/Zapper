using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zapper.Application.Abstractions;
using Zapper.Infrastructure.Repositories;

namespace Zapper.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddZapperPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Zapper")
            ?? throw new InvalidOperationException("Connection string 'Zapper' is required.");
        services.AddDbContext<ZapperDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISubMerchantRepository, SubMerchantRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IInboundNotificationRepository, InboundNotificationRepository>();
        services.AddScoped<IDailySettlementSummaryRepository, DailySettlementSummaryRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ZapperDbContext>());
        services.AddScoped<DevelopmentDataSeeder>();
        return services;
    }
}