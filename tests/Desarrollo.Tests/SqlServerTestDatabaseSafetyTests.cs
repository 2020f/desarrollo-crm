using Microsoft.Data.SqlClient;

namespace Desarrollo.Tests;

public sealed class SqlServerTestDatabaseSafetyTests
{
    [Theory]
    [InlineData("Desarrollo")]
    [InlineData("master")]
    [InlineData("Desarrollo_Test_not-a-guid")]
    [InlineData("Desarrollo_Test_0123456789abcdef0123456789abcdef_suffix")]
    public void Normal_or_unowned_database_names_are_refused(string name)
    {
        Assert.Throws<InvalidOperationException>(() => SqlServerTestDatabase.BuildConnectionString(name));
    }

    [Fact]
    public void Connection_always_targets_the_generated_test_catalog()
    {
        var name = $"Desarrollo_Test_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(SqlServerTestDatabase.BuildConnectionString(name));
        Assert.Equal(name, builder.InitialCatalog);
    }
}
