using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class SaltMonitorDbContextFactory : IDesignTimeDbContextFactory<SaltMonitorDbContext>
{
    public SaltMonitorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SaltMonitor")
            ?? "Host=localhost;Port=5432;Database=WaterFlexSaltMonitor;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<SaltMonitorDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new(options);
    }
}