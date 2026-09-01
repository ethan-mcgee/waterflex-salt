using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WaterFlex.SaltMonitor.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (e.g. <c>dotnet ef migrations</c>) to construct a
/// <see cref="SaltMonitorDbContext"/> outside of the application's normal DI startup.
/// </summary>
public sealed class SaltMonitorDbContextFactory : IDesignTimeDbContextFactory<SaltMonitorDbContext>
{
    /// <summary>Builds a context pointed at the connection string from the <c>ConnectionStrings__SaltMonitor</c> environment variable, falling back to a local development database.</summary>
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