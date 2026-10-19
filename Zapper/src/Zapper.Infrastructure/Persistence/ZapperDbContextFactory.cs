using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Zapper.Infrastructure.Persistence;

public sealed class ZapperDbContextFactory : IDesignTimeDbContextFactory<ZapperDbContext>
{
    public ZapperDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Zapper")
            ?? "Host=localhost;Port=5432;Database=zapper;Username=zapper;Password=zapper_local_only";
        var options = new DbContextOptionsBuilder<ZapperDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new ZapperDbContext(options);
    }
}