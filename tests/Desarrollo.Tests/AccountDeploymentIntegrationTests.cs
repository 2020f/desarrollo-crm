using System.Security.Claims;
using Desarrollo.Application.Common;
using Desarrollo.Domain.Organizations;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Desarrollo.Tests;

public sealed class AccountDeploymentIntegrationTests
{
    [Fact]
    public async Task Initial_admin_is_created_once_with_roles_and_no_existing_account_overwrite()
    {
        await WithDatabase(async database =>
        {
            await using var provider = Services(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountProvisioningService>();
            var request = Request();
            var result = await service.CreateTeamAsync(request, onlyIfEmpty: true);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True(await users.CheckPasswordAsync(result.User, request.Password));
            Assert.NotEqual(request.Password, result.User.PasswordHash);
            Assert.Equal(7, await db.OrganizationRoles.CountAsync());
            Assert.Single(await db.OrganizationMembers.ToListAsync());
            Assert.Single(await db.AuditLogs.Where(a => a.Action == "BootstrapOrganization").ToListAsync());
            var originalHash = result.User.PasswordHash;
            await Assert.ThrowsAsync<InvalidStateException>(() => service.CreateTeamAsync(Request(), onlyIfEmpty: true));
            await using var verify = database.NewDb();
            Assert.Equal(originalHash, (await verify.Users.SingleAsync()).PasswordHash);
            Assert.Single(await verify.Organizations.ToListAsync());
        });
    }

    [Fact]
    public async Task Failed_initial_admin_validation_leaves_no_partial_team_or_credentials()
    {
        await WithDatabase(async database =>
        {
            await using var provider = Services(database);
            await using var scope = provider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountProvisioningService>();
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateTeamAsync(Request() with { Password = "x" }, true));
            await using var verify = database.NewDb();
            Assert.False(await verify.Users.AnyAsync());
            Assert.False(await verify.Organizations.AnyAsync());
            Assert.False(await verify.OrganizationRoles.AnyAsync());
            Assert.False(await verify.OrganizationMembers.AnyAsync());
        });
    }

    [Fact]
    public async Task Concurrent_initialization_creates_only_one_administrator_and_one_organization()
    {
        await WithDatabase(async database =>
        {
            await using var provider = Services(database);
            async Task<bool> Attempt()
            {
                await using var scope = provider.CreateAsyncScope();
                try { await scope.ServiceProvider.GetRequiredService<AccountProvisioningService>().CreateTeamAsync(Request(), true); return true; }
                catch (InvalidStateException) { return false; }
            }
            var results = await Task.WhenAll(Attempt(), Attempt());
            Assert.Single(results, success => success);
            await using var verify = database.NewDb();
            Assert.Equal(1, await verify.Users.CountAsync());
            Assert.Equal(1, await verify.Organizations.CountAsync());
        });
    }

    [Fact]
    public async Task Cookie_validation_and_regenerated_principal_require_current_active_membership()
    {
        await WithDatabase(async database =>
        {
            await using var provider = Services(database);
            await using var scope = provider.CreateAsyncScope();
            var created = await scope.ServiceProvider.GetRequiredService<AccountProvisioningService>().CreateTeamAsync(Request(), true);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await ActiveAccountSession.IsActiveAsync(db, created.User.Id, created.Organization.Id));
            Assert.False(await ActiveAccountSession.IsActiveAsync(db, created.User.Id, Guid.NewGuid()));
            var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
            var refreshed = await factory.CreateAsync(created.User);
            Assert.Equal(created.Organization.Id.ToString(), refreshed.FindFirstValue("org_id"));
            var member = await db.OrganizationMembers.SingleAsync();
            member.IsActive = false; await db.SaveChangesAsync();
            Assert.False(await ActiveAccountSession.IsActiveAsync(db, created.User.Id, created.Organization.Id));
            Assert.Null((await factory.CreateAsync(created.User)).FindFirstValue("org_id"));
            member.IsActive = true; created.User.IsActive = false; await db.SaveChangesAsync();
            Assert.False(await ActiveAccountSession.IsActiveAsync(db, created.User.Id, created.Organization.Id));
        });
    }

    private static TeamAccountRequest Request() => new("Installation test", $"install-{Guid.NewGuid():N}@test.invalid",
        $"A7!{Guid.NewGuid():N}", "Installation team");
    private static ServiceProvider Services(SqlServerTestDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(SqlServerTestDatabase.BuildConnectionString(database.DatabaseName)));
        services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequiredLength = 8; options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false; options.Password.RequireDigit = true; options.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<AppDbContext>().AddClaimsPrincipalFactory<CrmClaimsPrincipalFactory>().AddDefaultTokenProviders();
        services.AddScoped<AccountProvisioningService>();
        return services.BuildServiceProvider();
    }
    private static async Task WithDatabase(Func<SqlServerTestDatabase, Task> test)
    {
        var database = new SqlServerTestDatabase();
        await database.InitializeAsync();
        try { await test(database); }
        finally { await database.DisposeAsync(); }
    }
}
