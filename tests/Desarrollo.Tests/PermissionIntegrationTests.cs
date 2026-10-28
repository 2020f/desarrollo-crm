using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public class PermissionIntegrationTests : IAsyncLifetime
{
    private string _dbName = $"Desarrollo_Test_{Guid.NewGuid():N}";
    private AppDbContext _db = null!;

    private AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlServerTestDatabase.BuildConnectionString(_dbName))
            .Options;
        return new AppDbContext(options);
    }

    public async Task InitializeAsync()
    {
        _db = NewDb();
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await SqlServerTestDatabase.DeleteOwnedDatabaseAsync(_db, _dbName);
        await _db.DisposeAsync();
    }

    private static ICurrentUser User(Guid? userId, Guid? orgId) => new StubCurrentUser(userId, orgId);

    [Fact]
    public async Task User_in_other_org_cannot_access_private_project()
    {
        var orgA = new Organization { Name = "Org A", Slug = "org-a" };
        var orgB = new Organization { Name = "Org B", Slug = "org-b" };
        _db.Organizations.AddRange(orgA, orgB);
        await _db.SaveChangesAsync();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        _db.Users.AddRange(
            new AppUser { Id = userA, UserName = "a@test.com", Email = "a@test.com", FullName = "User A" },
            new AppUser { Id = userB, UserName = "b@test.com", Email = "b@test.com", FullName = "User B" });
        await _db.SaveChangesAsync();

        var adminRole = new Role
        {
            OrganizationId = orgA.Id,
            Name = "Administrador de organización",
            Description = string.Empty,
            IsSystem = true,
            Permissions = Enum.GetValues<Permission>().ToList(),
        };
        _db.OrganizationRoles.Add(adminRole);
        await _db.SaveChangesAsync();

        _db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = orgA.Id, UserId = userA, RoleId = adminRole.Id, Name = "User A", IsActive = true },
            new OrganizationMember { OrganizationId = orgB.Id, UserId = userB, Name = "User B", IsActive = true });
        await _db.SaveChangesAsync();

        var project = new Project { OrganizationId = orgA.Id, Name = "Privado A", Code = "PRV", IsPrivate = true };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var permA = new PermissionService(_db, User(userA, orgA.Id));
        var permB = new PermissionService(_db, User(userB, orgB.Id));

        Assert.True(await permA.CanAccessProjectAsync(project.Id));
        Assert.False(await permB.CanAccessProjectAsync(project.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => permB.EnsureProjectPermissionAsync(project.Id, Permission.Leer));
    }

    [Fact]
    public async Task Same_org_member_can_read_workitem_in_public_project()
    {
        var org = new Organization { Name = "Org", Slug = "org-pub" };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        var admin = Guid.NewGuid();
        var member = Guid.NewGuid();
        _db.Users.AddRange(
            new AppUser { Id = admin, UserName = "admin@t.com", Email = "admin@t.com", FullName = "Admin" },
            new AppUser { Id = member, UserName = "member@t.com", Email = "member@t.com", FullName = "Member" });
        await _db.SaveChangesAsync();

        var adminRole = new Role { OrganizationId = org.Id, Name = "Admin", Description = string.Empty, IsSystem = true, Permissions = Enum.GetValues<Permission>().ToList() };
        _db.OrganizationRoles.Add(adminRole);
        await _db.SaveChangesAsync();

        _db.OrganizationMembers.AddRange(
            new OrganizationMember { OrganizationId = org.Id, UserId = admin, RoleId = adminRole.Id, Name = "Admin", IsActive = true },
            new OrganizationMember { OrganizationId = org.Id, UserId = member, Name = "Member", IsActive = true });
        await _db.SaveChangesAsync();

        var project = new Project { OrganizationId = org.Id, Name = "Público", Code = "PUB", IsPrivate = false };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var state = new Desarrollo.Domain.Work.WorkflowState { ProjectId = project.Id, Name = "Backlog", Order = 0, IsDefault = true };
        _db.WorkflowStates.Add(state);
        await _db.SaveChangesAsync();

        var task = new Desarrollo.Domain.Work.WorkItem
        {
            OrganizationId = org.Id, ProjectId = project.Id, Code = "PUB-1", Title = "Tarea",
            Type = Desarrollo.Domain.WorkItemType.Funcionalidad, StateId = state.Id,
            AuthorId = admin, CreatedBy = admin,
        };
        _db.WorkItems.Add(task);
        await _db.SaveChangesAsync();

        var permMember = new PermissionService(_db, User(member, org.Id));

        Assert.True(await permMember.CanAccessProjectAsync(project.Id));
        Assert.True(await permMember.CanReadWorkItemAsync(task.Id));
    }

    [Fact]
    public async Task Default_states_are_created_with_backlog_as_default()
    {
        var org = new Organization { Name = "Org", Slug = "org" };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();
        var project = new Project { OrganizationId = org.Id, Name = "P", Code = "P" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        await _db.EnsureDefaultStatesAsync(project.Id, org.Id);
        await _db.SaveChangesAsync();

        var states = await _db.WorkflowStates.Where(s => s.ProjectId == project.Id).ToListAsync();
        Assert.Equal(8, states.Count);
        Assert.Single(states, s => s.IsDefault);
        Assert.Equal("Backlog", states.Single(s => s.IsDefault).Name);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid? userId, Guid? orgId)
        {
            UserId = userId;
            OrganizationId = orgId;
        }

        public Guid? UserId { get; }
        public Guid? OrganizationId { get; }
        public string Name => "Test";
        public bool IsAuthenticated => UserId.HasValue;
    }
}
