using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

public sealed class SaltMonitorDbContextFactory : IDesignTimeDbContextFactory<SaltMonitorDbContext>
{
    public SaltMonitorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SaltMonitor")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=WaterFlexSaltMonitor;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SaltMonitorDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new(options);
    }
}