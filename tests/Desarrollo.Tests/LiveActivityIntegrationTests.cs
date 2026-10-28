using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class LiveActivityIntegrationTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    [Fact]
    public async Task Snapshot_counts_only_other_authors_after_last_read_and_does_not_mark_anything_read()
    {
        await using var db = database.NewDb();
        var owner = await SeedTeamAsync(db);
        var other = await AddMemberAsync(db, owner);
        var channel = await Channels(db, owner).GetOrCreateDirectAsync(other.UserId);
        var start = DateTime.UtcNow.AddMinutes(-10);
        var membership = await db.ChannelMembers.SingleAsync(m => m.ChannelId == channel.Id && m.UserId == owner.UserId);
        membership.LastReadAt = start.AddMinutes(1);
        var old = Message(channel.Id, other.UserId, start);
        var incoming = Message(channel.Id, other.UserId, start.AddMinutes(2));
        var own = Message(channel.Id, owner.UserId, start.AddMinutes(3));
        db.Messages.AddRange(old, incoming, own);
        var notification = Notify(owner, channel.Id, start);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        var memberCount = await db.ChannelMembers.CountAsync();
        var service = Service(db, owner);

        var first = await service.GetAsync();
        var second = await service.GetAsync();
        var activity = Assert.Single(first.Channels);
        Assert.Equal(channel.Id, activity.Id);
        Assert.Equal(own.Id, activity.LatestMessageId);
        Assert.Equal(owner.UserId, activity.LatestMessageAuthorId);
        Assert.Equal(own.CreatedAt, activity.LatestMessageAt);
        // Both messages arrive between observations. The own latest message must
        // not suppress the independently identifiable incoming message/sound.
        Assert.Equal(incoming.Id, activity.LatestIncomingMessageId);
        Assert.Equal(incoming.CreatedAt, activity.LatestIncomingMessageAt);
        Assert.Equal(DateTimeKind.Utc, activity.LatestIncomingMessageAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, activity.LatestMessageAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, first.ObservedAt.Kind);
        Assert.Equal(1, activity.UnreadCount);
        Assert.Equal(1, first.Notifications.UnreadCount);
        Assert.Equal(notification.Id, Assert.Single(first.Notifications.Items).Id);
        Assert.Equal(DateTimeKind.Utc, first.Notifications.Items[0].CreatedAt.Kind);
        Assert.Equal(activity, Assert.Single(second.Channels));
        Assert.Equal(memberCount, await db.ChannelMembers.CountAsync());
        Assert.Equal(start.AddMinutes(1), (await db.ChannelMembers.AsNoTracking()
            .SingleAsync(m => m.Id == membership.Id)).LastReadAt);
        Assert.False((await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == notification.Id)).IsRead);
    }

    [Fact]
    public async Task Snapshot_excludes_foreign_and_private_channels_and_deleted_thread_activity()
    {
        await using var db = database.NewDb();
        var owner = await SeedTeamAsync(db);
        var other = await AddMemberAsync(db, owner);
        var foreign = await SeedTeamAsync(db);
        var readable = await Channels(db, owner).CreateAsync(owner.ProjectId, "Project chat", null);
        var privateChat = await Channels(db, other).CreateAsync(null, "Other member private chat", null);
        var foreignChat = await Channels(db, foreign).CreateAsync(null, "Other organization", null);
        var at = DateTime.UtcNow.AddMinutes(-2);
        var visible = Message(readable.Id, other.UserId, at);
        var deletedRoot = Message(readable.Id, other.UserId, at.AddSeconds(1)); deletedRoot.IsDeleted = true;
        var replyToDeletedRoot = Message(readable.Id, other.UserId, at.AddSeconds(2)); replyToDeletedRoot.ThreadRootId = deletedRoot.Id;
        var deleted = Message(readable.Id, other.UserId, at.AddSeconds(3)); deleted.IsDeleted = true;
        var hiddenPrivate = Message(privateChat.Id, other.UserId, at.AddSeconds(4));
        var hiddenForeign = Message(foreignChat.Id, foreign.UserId, at.AddSeconds(5));
        db.Messages.AddRange(visible, deletedRoot, replyToDeletedRoot, deleted, hiddenPrivate, hiddenForeign);
        db.Notifications.AddRange(Notify(owner, readable.Id, at), Notify(owner, privateChat.Id, at),
            new Notification { OrganizationId = foreign.OrgId, UserId = owner.UserId, EntityType = "Channel",
                EntityId = foreignChat.Id, Message = "Hidden", DedupKey = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();
        // Reading a project channel must not create the optional read receipt row.
        db.ChannelMembers.Remove(await db.ChannelMembers.SingleAsync(m => m.ChannelId == readable.Id && m.UserId == owner.UserId));
        await db.SaveChangesAsync();

        var snapshot = await Service(db, owner).GetAsync();
        var activity = Assert.Single(snapshot.Channels);
        Assert.Equal(readable.Id, activity.Id);
        Assert.Equal(visible.Id, activity.LatestMessageId);
        Assert.Equal(visible.CreatedAt, activity.ChangedAt);
        Assert.Equal(1, activity.UnreadCount);
        Assert.Equal(1, snapshot.Notifications.UnreadCount);
        Assert.Single(snapshot.Notifications.Items);
        Assert.False(await db.ChannelMembers.AnyAsync(m => m.ChannelId == readable.Id && m.UserId == owner.UserId));
    }

    [Fact]
    public async Task Editing_an_older_message_changes_activity_without_replacing_the_latest_message()
    {
        await using var db = database.NewDb();
        var owner = await SeedTeamAsync(db);
        var channel = await Channels(db, owner).CreateAsync(null, "Editable chat", null);
        var at = DateTime.UtcNow.AddMinutes(-5);
        var older = Message(channel.Id, owner.UserId, at);
        var latest = Message(channel.Id, owner.UserId, at.AddMinutes(1));
        db.Messages.AddRange(older, latest); await db.SaveChangesAsync();
        var service = Service(db, owner);
        var before = Assert.Single((await service.GetAsync()).Channels);

        await Channels(db, owner).EditAsync(older.Id, "Edited older text");
        var after = Assert.Single((await service.GetAsync()).Channels);
        Assert.Equal(latest.Id, before.LatestMessageId);
        Assert.Equal(latest.Id, after.LatestMessageId);
        Assert.Equal(latest.CreatedAt, after.LatestMessageAt);
        Assert.True(after.ChangedAt > before.ChangedAt);
        Assert.Equal(0, after.UnreadCount);
        Assert.Null(after.LatestIncomingMessageId);
        Assert.Null(after.LatestIncomingMessageAt);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("organization-member")]
    [InlineData("channel-member")]
    [InlineData("archived-channel")]
    public async Task Each_snapshot_rechecks_account_membership_and_channel_access(string revoked)
    {
        await using var db = database.NewDb();
        var owner = await SeedTeamAsync(db);
        var channel = await Channels(db, owner).CreateAsync(null, "Revocable chat", null);
        db.Messages.Add(Message(channel.Id, owner.UserId, DateTime.UtcNow));
        db.Notifications.Add(Notify(owner, channel.Id, DateTime.UtcNow)); await db.SaveChangesAsync();
        var service = Service(db, owner);
        Assert.Single((await service.GetAsync()).Channels);
        if (revoked == "user") (await db.Users.SingleAsync(u => u.Id == owner.UserId)).IsActive = false;
        else if (revoked == "organization-member")
            (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == owner.OrgId && m.UserId == owner.UserId)).IsActive = false;
        else if (revoked == "channel-member")
            db.ChannelMembers.Remove(await db.ChannelMembers.SingleAsync(m => m.ChannelId == channel.Id && m.UserId == owner.UserId));
        else (await db.Channels.SingleAsync(c => c.Id == channel.Id)).IsArchived = true;
        await db.SaveChangesAsync();

        if (revoked is "user" or "organization-member")
            await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync());
        else
        {
            var hidden = await service.GetAsync();
            Assert.Empty(hidden.Channels); Assert.Empty(hidden.Notifications.Items);
            Assert.Equal(0, hidden.Notifications.UnreadCount);
        }
    }

    [Fact]
    public async Task Project_role_revocation_removes_project_chat_activity_and_notifications()
    {
        await using var db = database.NewDb();
        var owner = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, owner);
        var channel = await Channels(db, owner).CreateAsync(owner.ProjectId, "Project chat", null);
        db.Messages.Add(Message(channel.Id, owner.UserId, DateTime.UtcNow));
        db.Notifications.Add(Notify(reader, channel.Id, DateTime.UtcNow)); await db.SaveChangesAsync();
        var service = Service(db, reader);
        Assert.Single((await service.GetAsync()).Channels);
        var roleId = await db.ProjectMembers.Where(m => m.ProjectId == owner.ProjectId && m.UserId == reader.UserId)
            .Select(m => m.RoleId).SingleAsync();
        (await db.OrganizationRoles.SingleAsync(r => r.Id == roleId)).Permissions = [];
        await db.SaveChangesAsync();

        var hidden = await service.GetAsync();
        Assert.Empty(hidden.Channels); Assert.Empty(hidden.Notifications.Items);
        Assert.Equal(0, hidden.Notifications.UnreadCount);
    }

    [Fact]
    public async Task Notification_sample_is_bounded_while_unread_total_remains_exact()
    {
        await using var db = database.NewDb();
        var owner = await SeedTeamAsync(db);
        var channel = await Channels(db, owner).CreateAsync(null, "Notification chat", null);
        var at = DateTime.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 103; i++)
        {
            var notification = Notify(owner, channel.Id, at.AddSeconds(i));
            notification.IsRead = i == 102;
            db.Notifications.Add(notification);
        }
        await db.SaveChangesAsync();

        var snapshot = await Service(db, owner).GetAsync();
        Assert.Equal(102, snapshot.Notifications.UnreadCount);
        Assert.Equal(100, snapshot.Notifications.Items.Count);
        Assert.Equal(at.AddSeconds(101), snapshot.Notifications.Items[0].CreatedAt);
        Assert.Equal(at.AddSeconds(2), snapshot.Notifications.Items[^1].CreatedAt);
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.OrganizationId == owner.OrgId && n.IsRead));
    }

    private static LiveActivityService Service(AppDbContext db, Team team)
    {
        var user = new TestUser(team.UserId, team.OrgId);
        var permissions = new PermissionService(db, user);
        return new(db, user, new ChannelService(db, user, permissions), new NotificationService(db, user, permissions));
    }

    private static ChannelService Channels(AppDbContext db, Team team)
    {
        var user = new TestUser(team.UserId, team.OrgId);
        return new(db, user, new PermissionService(db, user));
    }

    private static Message Message(Guid channelId, Guid authorId, DateTime at) => new()
    { ChannelId = channelId, AuthorId = authorId, Body = "SQL snapshot test", CreatedAt = at, UpdatedAt = at };

    private static Notification Notify(Team team, Guid channelId, DateTime at) => new()
    { OrganizationId = team.OrgId, UserId = team.UserId, EntityType = "Channel", EntityId = channelId,
        Message = "SQL snapshot notification", CreatedAt = at, UpdatedAt = at, DedupKey = Guid.NewGuid().ToString("N") };

    private static async Task<Team> SeedTeamAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = "Activity tests", Slug = $"activity-{suffix}" };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"activity-{suffix}@test.invalid", FullName = "Activity administrator" };
        var role = new Role { OrganizationId = organization.Id, Name = "Administrator", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(organization); db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = organization.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName });
        var project = new Project { OrganizationId = organization.Id, Code = "ACT", Name = "Private activity project", IsPrivate = true };
        db.Projects.Add(project); await db.SaveChangesAsync();
        return new(organization.Id, project.Id, user.Id);
    }

    private static async Task<Team> AddMemberAsync(AppDbContext db, Team team)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"activity-member-{suffix}@test.invalid", FullName = "Activity member" };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Reader-{suffix}", Permissions = [Permission.Leer, Permission.Comentar] };
        db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = team.OrgId, UserId = user.Id, RoleId = role.Id, Name = user.FullName };
        db.OrganizationMembers.Add(member);
        db.ProjectMembers.Add(new() { ProjectId = team.ProjectId, OrganizationMemberId = member.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName });
        await db.SaveChangesAsync();
        return team with { UserId = user.Id };
    }

    private sealed record Team(Guid OrgId, Guid ProjectId, Guid UserId);
    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    { public string Name => "SQL activity test"; public bool IsAuthenticated => true; }
}
