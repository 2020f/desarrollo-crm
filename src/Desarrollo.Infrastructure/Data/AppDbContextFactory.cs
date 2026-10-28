using Desarrollo.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Desarrollo.Infrastructure.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("DESARROLLO_CONNECTION_STRING")
            ?? "Server=localhost;Database=Desarrollo;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(conn, o => o.MigrationsAssembly("Desarrollo.Infrastructure"))
            .Options;

        return new AppDbContext(options);
    }
}
