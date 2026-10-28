using Desarrollo.Application.Abstractions;
using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class NotificationChatSecurityIntegrationTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    [Theory]
    [InlineData("channel-member")]
    [InlineData("organization-member")]
    [InlineData("user")]
    [InlineData("archived-channel")]
    public async Task Historical_message_and_channel_notifications_recheck_membership_and_archive(string change)
    {
        await using var db = database.NewDb();
        var suffix = Guid.NewGuid().ToString("N");
        var org = new Organization { Name = "Chat notification test", Slug = $"chat-notification-{suffix}" };
        var reader = new AppUser { Id = Guid.NewGuid(), UserName = $"reader-{suffix}@test.invalid", FullName = "Chat recipient" };
        var administrator = new AppUser { Id = Guid.NewGuid(), UserName = $"admin-{suffix}@test.invalid", FullName = "Nonparticipant admin" };
        var role = new Role { OrganizationId = org.Id, Name = "Reader", Permissions = new() { Permission.Leer, Permission.Comentar } };
        var adminRole = new Role { OrganizationId = org.Id, Name = "Admin", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(org); db.Users.AddRange(reader, administrator); db.OrganizationRoles.AddRange(role, adminRole);
        await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = org.Id, UserId = reader.Id, RoleId = role.Id, Name = reader.FullName, IsActive = true };
        db.OrganizationMembers.AddRange(member, new() { OrganizationId = org.Id, UserId = administrator.Id, RoleId = adminRole.Id, Name = administrator.FullName, IsActive = true });
        var channel = new Channel { OrganizationId = org.Id, Type = ChannelType.Directo, Name = "Direct channel", Topic = "Private channel text" };
        db.Channels.Add(channel); await db.SaveChangesAsync();
        var participation = new ChannelMember { ChannelId = channel.Id, UserId = reader.Id };
        db.ChannelMembers.Add(participation);
        var message = new Message { ChannelId = channel.Id, AuthorId = reader.Id, Body = "Private message text" };
        db.Messages.Add(message); await db.SaveChangesAsync();
        foreach (var userId in new[] { reader.Id, administrator.Id })
        {
            db.Notifications.Add(new Notification { OrganizationId = org.Id, UserId = userId, EntityType = "Message", EntityId = message.Id,
                Event = NotificationEvent.Respuesta, Message = message.Body, DedupKey = $"message:{userId}:{suffix}" });
            db.Notifications.Add(new Notification { OrganizationId = org.Id, UserId = userId, EntityType = "Channel", EntityId = channel.Id,
                Event = NotificationEvent.Mencion, Message = channel.Topic!, DedupKey = $"channel:{userId}:{suffix}" });
        }
        await db.SaveChangesAsync();
        var current = new TestUser(reader.Id, org.Id);
        var notifications = new NotificationService(db, current, new PermissionService(db, current));
        Assert.Equal(2, (await notifications.ListAsync(false, 1, 20)).Total);
        Assert.Equal(2, await notifications.UnreadCountAsync());
        var admin = new TestUser(administrator.Id, org.Id);
        var adminNotifications = new NotificationService(db, admin, new PermissionService(db, admin));
        Assert.Empty((await adminNotifications.ListAsync(false, 1, 20)).Items);
        Assert.Equal(0, await adminNotifications.UnreadCountAsync());
        if (change == "channel-member") db.ChannelMembers.Remove(participation);
        else if (change == "organization-member") member.IsActive = false;
        else if (change == "user") reader.IsActive = false;
        else channel.IsArchived = true;
        await db.SaveChangesAsync();
        var hidden = await notifications.ListAsync(false, 1, 20);
        Assert.Empty(hidden.Items); Assert.Equal(0, hidden.Total); Assert.Equal(0, await notifications.UnreadCountAsync());
    }

    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    { public string Name => "SQL chat notification test"; public bool IsAuthenticated => true; }
}
