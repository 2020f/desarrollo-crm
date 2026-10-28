using Desarrollo.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

/// <summary>Real SQL Server fixture. The configured catalog is always replaced by a unique test database.</summary>
public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    public string DatabaseName { get; } = $"Desarrollo_Test_{Guid.NewGuid():N}";

    public AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(BuildConnectionString(DatabaseName))
        .Options);

    public static string BuildConnectionString(string databaseName)
    {
        AssertOwnedName(databaseName);
        var configured = Environment.GetEnvironmentVariable("DESARROLLO_TEST_CONNECTION_STRING")
            ?? "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True";
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = databaseName };
        if (!string.IsNullOrEmpty(builder.AttachDBFilename))
            throw new InvalidOperationException("Tests do not support attaching database files.");
        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewDb();
        await DeleteOwnedDatabaseAsync(db, DatabaseName);
    }

    internal static async Task DeleteOwnedDatabaseAsync(AppDbContext db, string expectedName)
    {
        AssertOwnedName(expectedName);
        if (!StringComparer.Ordinal.Equals(db.Database.GetDbConnection().Database, expectedName))
            throw new InvalidOperationException("Refusing to delete an unexpected database.");
        if (!await db.Database.CanConnectAsync()) return;

        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT DB_NAME()";
            var actualName = (string?)await command.ExecuteScalarAsync();
            if (!StringComparer.Ordinal.Equals(actualName, expectedName))
                throw new InvalidOperationException("SQL Server returned an unexpected database; deletion refused.");
        }
        await db.Database.CloseConnectionAsync();
        await db.Database.EnsureDeletedAsync();
    }

    private static void AssertOwnedName(string name)
    {
        const string prefix = "Desarrollo_Test_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new InvalidOperationException("Only generated Desarrollo_Test_GUID databases are allowed.");
    }
}
