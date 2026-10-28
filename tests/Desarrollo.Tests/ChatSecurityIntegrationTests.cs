using System.Security.Claims;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Desarrollo.Web.Hubs;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class ChatSecurityIntegrationTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    [Theory]
    [InlineData("user")]
    [InlineData("organization-member")]
    public async Task Every_chat_operation_revalidates_current_account_and_organization_membership(string revoked)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var target = await AddMemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var service = Service(db, team);
        var channel = await service.CreateAsync(null, "Private before revocation", null);
        var message = await service.SendAsync(channel.Id, "Original body", null);
        var count = await db.Channels.CountAsync(c => c.OrganizationId == team.OrgId);
        if (revoked == "user") (await db.Users.FirstAsync(u => u.Id == team.UserId)).IsActive = false;
        else (await db.OrganizationMembers.FirstAsync(m => m.OrganizationId == team.OrgId && m.UserId == team.UserId)).IsActive = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(null, "Blocked", null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetOrCreateDirectAsync(target.UserId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMessagesAsync(channel.Id, 1, 50));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SendAsync(channel.Id, "Blocked", null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EditAsync(message.Id, "Blocked"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkReadAsync(channel.Id));
        Assert.Equal(count, await db.Channels.CountAsync(c => c.OrganizationId == team.OrgId));
        Assert.Equal("Original body", (await db.Messages.AsNoTracking().SingleAsync(m => m.Id == message.Id)).Body);
    }

    [Fact]
    public async Task Organization_admin_cannot_list_read_write_or_mark_read_other_members_private_conversations()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var sender = await AddMemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var recipient = await AddMemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var senderService = Service(db, sender);
        var direct = await senderService.GetOrCreateDirectAsync(recipient.UserId);
        var privateChannel = await senderService.CreateAsync(null, "Only explicit members", null);
        var message = await senderService.SendAsync(privateChannel.Id, "Private text", null);
        var administrator = Service(db, team);
        Assert.DoesNotContain(await administrator.ListAsync(null), c => c.Id == direct.Id || c.Id == privateChannel.Id);
        foreach (var id in new[] { direct.Id, privateChannel.Id })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => administrator.GetMessagesAsync(id, 1, 50));
            await Assert.ThrowsAsync<ForbiddenException>(() => administrator.SendAsync(id, "Blocked", null));
            await Assert.ThrowsAsync<ForbiddenException>(() => administrator.MarkReadAsync(id));
            Assert.False(await db.ChannelMembers.AnyAsync(m => m.ChannelId == id && m.UserId == team.UserId));
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => administrator.EditAsync(message.Id, "Blocked"));
        Assert.Contains(await senderService.ListAsync(null), c => c.Id == privateChannel.Id && c.Name == "Only explicit members");
    }

    [Fact]
    public async Task Removing_private_channel_membership_immediately_blocks_its_own_author_and_does_not_rejoin_on_read()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var service = Service(db, team);
        var channel = await service.CreateAsync(null, "Private", null);
        var message = await service.SendAsync(channel.Id, "Before leaving", null);
        db.ChannelMembers.Remove(await db.ChannelMembers.SingleAsync(m => m.ChannelId == channel.Id && m.UserId == team.UserId));
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await service.ListAsync(null), c => c.Id == channel.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMessagesAsync(channel.Id, 1, 50));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EditAsync(message.Id, "After leaving"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkReadAsync(channel.Id));
        Assert.False(await db.ChannelMembers.AnyAsync(m => m.ChannelId == channel.Id && m.UserId == team.UserId));
    }

    [Fact]
    public async Task Selecting_another_real_organization_does_not_authorize_editing_a_message_from_the_first()
    {
        await using var db = database.NewDb();
        var first = await SeedTeamAsync(db);
        var second = await SeedTeamAsync(db);
        var owner = Service(db, first);
        var channel = await owner.CreateAsync(null, "First organization", null);
        var message = await owner.SendAsync(channel.Id, "Original", null);
        db.OrganizationMembers.Add(new() { OrganizationId = second.OrgId, UserId = first.UserId, Name = "Same account", IsActive = true });
        await db.SaveChangesAsync();
        var switched = Service(db, first with { OrgId = second.OrgId });
        await Assert.ThrowsAsync<ForbiddenException>(() => switched.EditAsync(message.Id, "Cross organization"));
        await Assert.ThrowsAsync<ForbiddenException>(() => switched.GetMessagesAsync(channel.Id, 1, 50));
        await Assert.ThrowsAsync<ForbiddenException>(() => switched.ListAsync(first.ProjectId));
        Assert.Equal("Original", (await db.Messages.AsNoTracking().SingleAsync(m => m.Id == message.Id)).Body);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("organization-member")]
    public async Task Direct_target_must_be_an_active_account_and_active_member_of_the_same_organization(string revoked)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var target = await AddMemberAsync(db, team, Permission.Leer);
        if (revoked == "user") (await db.Users.SingleAsync(u => u.Id == target.UserId)).IsActive = false;
        else (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == team.OrgId && m.UserId == target.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, team).GetOrCreateDirectAsync(target.UserId));
        var foreign = await SeedTeamAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, team).GetOrCreateDirectAsync(foreign.UserId));
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, team).GetOrCreateDirectAsync(team.UserId));
        Assert.False(await db.Channels.AnyAsync(c => c.OrganizationId == team.OrgId));
    }

    [Fact]
    public async Task A_direct_conversation_has_exact_membership_and_is_reused_for_its_pair()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var target = await AddMemberAsync(db, team, Permission.Leer);
        var first = await Service(db, team).GetOrCreateDirectAsync(target.UserId);
        var again = await Service(db, target).GetOrCreateDirectAsync(team.UserId);
        Assert.Equal(first.Id, again.Id);
        var memberIds = await db.ChannelMembers.Where(m => m.ChannelId == first.Id).Select(m => m.UserId).ToListAsync();
        Assert.Equal(2, memberIds.Count); Assert.Contains(team.UserId, memberIds); Assert.Contains(target.UserId, memberIds);
        Assert.Contains(await Service(db, target).ListAsync(null), c => c.Id == first.Id && c.DirectUserId == team.UserId);
    }

    [Fact]
    public async Task Project_reader_can_read_but_losing_comment_permission_blocks_send_and_edit_even_for_author()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var member = await AddMemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var channel = await Service(db, team).CreateAsync(team.ProjectId, "Project", null);
        var service = Service(db, member);
        var message = await service.SendAsync(channel.Id, "Before role change", null);
        var role = await MemberRoleAsync(db, member); role.Permissions = [Permission.Leer]; await db.SaveChangesAsync();
        Assert.Single((await service.GetMessagesAsync(channel.Id, 1, 50)).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SendAsync(channel.Id, "Blocked", null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EditAsync(message.Id, "Blocked"));
        await service.MarkReadAsync(channel.Id);
        Assert.NotNull((await db.ChannelMembers.SingleAsync(m => m.ChannelId == channel.Id && m.UserId == member.UserId)).LastReadAt);
    }

    [Fact]
    public async Task Private_project_membership_revocation_blocks_all_channel_operations_despite_old_channel_membership()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var member = await AddMemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var channel = await Service(db, team).CreateAsync(team.ProjectId, "Project", null);
        var service = Service(db, member);
        var message = await service.SendAsync(channel.Id, "Before revocation", null); await service.MarkReadAsync(channel.Id);
        (await db.ProjectMembers.SingleAsync(m => m.ProjectId == team.ProjectId && m.UserId == member.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await service.ListAsync(null), c => c.Id == channel.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMessagesAsync(channel.Id, 1, 50));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SendAsync(channel.Id, "Blocked", null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EditAsync(message.Id, "Blocked"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkReadAsync(channel.Id));
    }

    [Fact]
    public async Task Public_project_membership_fallback_is_the_same_as_the_central_permission_policy()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var member = await AddMemberAsync(db, team, Permission.Leer);
        var channel = await Service(db, team).CreateAsync(team.ProjectId, "Public project", null);
        (await db.Projects.SingleAsync(p => p.Id == team.ProjectId)).IsPrivate = false;
        (await db.ProjectMembers.SingleAsync(m => m.ProjectId == team.ProjectId && m.UserId == member.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        var current = new TestUser(member.UserId, member.OrgId);
        Assert.True(await new PermissionService(db, current).CanAccessProjectAsync(team.ProjectId));
        var sent = await Service(db, member).SendAsync(channel.Id, "Allowed by public policy", null);
        Assert.Equal("Allowed by public policy", sent.Body);
    }

    [Fact]
    public async Task Archived_channel_is_hidden_and_cannot_be_read_edited_sent_to_or_marked_read()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db); var service = Service(db, team);
        var channel = await service.CreateAsync(null, "Archived", null);
        var message = await service.SendAsync(channel.Id, "History", null);
        (await db.Channels.SingleAsync(c => c.Id == channel.Id)).IsArchived = true; await db.SaveChangesAsync();
        Assert.DoesNotContain(await service.ListAsync(null), c => c.Id == channel.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetMessagesAsync(channel.Id, 1, 50));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SendAsync(channel.Id, "Blocked", null));
        await Assert.ThrowsAsync<NotFoundException>(() => service.EditAsync(message.Id, "Blocked"));
        await Assert.ThrowsAsync<NotFoundException>(() => service.MarkReadAsync(channel.Id));
    }

    [Fact]
    public async Task Archived_project_retains_authorized_history_but_forbids_chat_mutations_and_deleted_project_is_inaccessible()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db); var service = Service(db, team);
        var channel = await service.CreateAsync(team.ProjectId, "Project", null);
        var message = await service.SendAsync(channel.Id, "History", null);
        var project = await db.Projects.SingleAsync(p => p.Id == team.ProjectId); project.Status = ProjectStatus.Archivado; await db.SaveChangesAsync();
        Assert.Single((await service.GetMessagesAsync(channel.Id, 1, 50)).Items);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SendAsync(channel.Id, "Blocked", null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.EditAsync(message.Id, "Blocked"));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(team.ProjectId, "Blocked", null));
        await service.MarkReadAsync(channel.Id);
        project.IsDeleted = true; await db.SaveChangesAsync();
        Assert.DoesNotContain(await service.ListAsync(null), c => c.Id == channel.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetMessagesAsync(channel.Id, 1, 50));
    }

    [Fact]
    public async Task Thread_root_must_be_current_top_level_message_from_the_same_channel()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db); var service = Service(db, team);
        var first = await service.CreateAsync(null, "First", null); var second = await service.CreateAsync(null, "Second", null);
        var root = await service.SendAsync(first.Id, "Root", null);
        var reply = await service.SendAsync(first.Id, "Reply", root.Id);
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(second.Id, "Cross channel root", root.Id));
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(first.Id, "Nested root", reply.Id));
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(first.Id, "Missing root", Guid.NewGuid()));
        var deleted = await db.Messages.SingleAsync(m => m.Id == root.Id); deleted.IsDeleted = true; deleted.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.Empty((await service.GetMessagesAsync(first.Id, 1, 50)).Items);
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(first.Id, "Deleted root", root.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.EditAsync(root.Id, "Edit deleted"));
        await Assert.ThrowsAsync<ValidationException>(() => service.EditAsync(reply.Id, "Edit hidden reply"));
        Assert.Equal(2, await db.Messages.CountAsync(m => m.ChannelId == first.Id));
        Assert.False(await db.Messages.AnyAsync(m => m.ChannelId == second.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("Null\0body")]
    public async Task Invalid_messages_and_names_are_rejected_without_changes(string body)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db); var service = Service(db, team);
        var channel = await service.CreateAsync(null, "Valid", null); var message = await service.SendAsync(channel.Id, "Keep", null);
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(channel.Id, body, null));
        await Assert.ThrowsAsync<ValidationException>(() => service.EditAsync(message.Id, body));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(null, "\nBad", null));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(null, new string('a', 201), null));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(null, "Valid", new string('t', 1001)));
        Assert.Equal("Keep", (await db.Messages.AsNoTracking().SingleAsync(m => m.Id == message.Id)).Body);
    }

    [Fact]
    public async Task Message_length_and_pagination_are_bounded_without_integer_overflow()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db); var service = Service(db, team);
        var channel = await service.CreateAsync(null, "Bounded", null);
        var maximum = await service.SendAsync(channel.Id, new string('x', 20_000), null);
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(channel.Id, new string('x', 20_001), null));
        await Assert.ThrowsAsync<ValidationException>(() => service.EditAsync(maximum.Id, new string('x', 20_001)));
        db.Messages.AddRange(Enumerable.Range(0, 104).Select(i => new Message { ChannelId = channel.Id, AuthorId = team.UserId, Body = $"Message {i}", CreatedAt = DateTime.UtcNow.AddSeconds(i) }));
        await db.SaveChangesAsync();
        var first = await service.GetMessagesAsync(channel.Id, 1, int.MaxValue);
        Assert.Equal(100, first.PageSize); Assert.Equal(100, first.Items.Count); Assert.Equal(105, first.Total);
        Assert.Equal(5, (await service.GetMessagesAsync(channel.Id, 2, 100)).Items.Count);
        var normalized = await service.GetMessagesAsync(channel.Id, -5, 0); Assert.Equal(1, normalized.Page); Assert.Equal(1, normalized.PageSize);
        await Assert.ThrowsAsync<ValidationException>(() => service.GetMessagesAsync(channel.Id, int.MaxValue, 100));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("user")]
    [InlineData("organization-member")]
    [InlineData("foreign-organization")]
    [InlineData("invalid-claim")]
    [InlineData("unauthenticated")]
    public async Task SignalR_connect_checks_current_database_membership_before_joining_any_group(string condition)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        if (condition == "user") (await db.Users.SingleAsync(u => u.Id == team.UserId)).IsActive = false;
        if (condition == "organization-member") (await db.OrganizationMembers.SingleAsync(m => m.UserId == team.UserId && m.OrganizationId == team.OrgId)).IsActive = false;
        await db.SaveChangesAsync();
        var orgClaim = condition == "foreign-organization" ? Guid.NewGuid().ToString() : team.OrgId.ToString();
        var userClaim = condition == "invalid-claim" ? "not-a-guid" : team.UserId.ToString();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userClaim), new Claim("org_id", orgClaim)], condition == "unauthenticated" ? null : "Test");
        var caller = new TestHubCallerContext(new ClaimsPrincipal(identity)); var groups = new RecordingGroups();
        var hub = new AppHub(db) { Context = caller, Groups = groups };
        await hub.OnConnectedAsync();
        Assert.Equal(condition != "active", caller.Aborted);
        if (condition == "active") Assert.Equal(new[] { $"org:{team.OrgId:D}", $"user:{team.UserId:D}" }, groups.Joined);
        else Assert.Empty(groups.Joined);
    }

    private static ChannelService Service(AppDbContext db, Team team)
    {
        var user = new TestUser(team.UserId, team.OrgId);
        return new(db, user, new PermissionService(db, user));
    }

    private static async Task<Team> SeedTeamAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = "Chat tests", Slug = $"chat-{suffix}" };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"chat-{suffix}@test.invalid", FullName = "Chat administrator" };
        var role = new Role { OrganizationId = organization.Id, Name = "Administrator", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(organization); db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = organization.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName });
        var project = new Project { OrganizationId = organization.Id, Code = "CHAT", Name = "Private chat project", IsPrivate = true };
        db.Projects.Add(project); await db.SaveChangesAsync();
        return new(organization.Id, project.Id, user.Id);
    }

    private static async Task<Team> AddMemberAsync(AppDbContext db, Team team, params Permission[] permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"chat-member-{suffix}@test.invalid", FullName = "Chat member" };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Role-{suffix}", Permissions = permissions.ToList() };
        db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = team.OrgId, UserId = user.Id, RoleId = role.Id, Name = user.FullName };
        db.OrganizationMembers.Add(member);
        db.ProjectMembers.Add(new() { ProjectId = team.ProjectId, OrganizationMemberId = member.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName });
        await db.SaveChangesAsync(); return team with { UserId = user.Id };
    }

    private static async Task<Role> MemberRoleAsync(AppDbContext db, Team team)
    {
        var roleId = await db.ProjectMembers.Where(m => m.ProjectId == team.ProjectId && m.UserId == team.UserId).Select(m => m.RoleId).SingleAsync();
        return await db.OrganizationRoles.SingleAsync(r => r.Id == roleId);
    }

    private sealed record Team(Guid OrgId, Guid ProjectId, Guid UserId);
    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    { public string Name => "SQL chat test"; public bool IsAuthenticated => true; }

    private sealed class TestHubCallerContext(ClaimsPrincipal user) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => "sql-test-connection";
        public override string? UserIdentifier => user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class RecordingGroups : IGroupManager
    {
        public List<string> Joined { get; } = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { Joined.Add(groupName); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
