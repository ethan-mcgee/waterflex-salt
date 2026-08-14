using Npgsql;
using Testcontainers.PostgreSql;

namespace WaterFlex.SaltMonitor.Tests;

internal static class TestPostgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartAsync);

    public static string GetConnectionString(string databaseName) =>
        GetConnectionStringAsync(databaseName).GetAwaiter().GetResult();

    public static async Task<string> GetConnectionStringAsync(string databaseName)
    {
        var container = await Container.Value.ConfigureAwait(false);
        var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = databaseName,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("postgres")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await container.StartAsync().ConfigureAwait(false);
        return container;
    }
}
