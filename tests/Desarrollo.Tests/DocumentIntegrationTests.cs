using System.Text.Json.Nodes;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Domain.Docs;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class DocumentIntegrationTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    private const string Rich = """{"type":"doc","content":[{"type":"heading","attrs":{"level":2,"textAlign":"center"},"content":[{"type":"text","text":"Decisión","marks":[{"type":"bold"}]}]},{"type":"paragraph","attrs":{"textAlign":null},"content":[{"type":"text","text":"Referencia","marks":[{"type":"link","attrs":{"href":"https://example.invalid/decision","target":null,"rel":null,"class":null}}]}]},{"type":"taskList","content":[{"type":"taskItem","attrs":{"checked":true},"content":[{"type":"paragraph","content":[{"type":"text","text":"Validar"}]}]}]}]}""";

    [Fact]
    public async Task New_private_documents_are_author_only_even_for_an_organization_admin()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var author = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear);
        var service = Service(db, author);
        var doc = await service.CreateAsync(team.ProjectId, "Private decision", null, Rich, contentFormat: "tiptap");
        Assert.Equal(DocumentVisibility.Privada, doc.Visibility);
        Assert.Equal(team.ProjectId, doc.ProjectId);
        Assert.Equal("tiptap", doc.ContentFormat);
        Assert.True(doc.CanEdit); Assert.True(doc.CanManageAccess);
        Assert.Empty(doc.ReaderIds!);
        Assert.Equal(8, Convert.FromBase64String(doc.RowVersion).Length);
        var ownList = Assert.Single(await service.ListAsync(team.ProjectId));
        Assert.Null(ownList.Content);
        Assert.Empty(await Service(db, team).ListAsync(team.ProjectId));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, team).GetAsync(doc.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, team).GetVersionsAsync(doc.Id));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Rich), JsonNode.Parse((await service.GetAsync(doc.Id)).Content!)));
    }

    [Fact]
    public async Task Selected_readers_need_access_and_only_the_author_manages_audience()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var author = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var editor = await AddMemberAsync(db, team, Permission.Leer, Permission.Editar);
        var outsider = await AddMemberAsync(db, team, Permission.Leer);
        var doc = await Service(db, author).CreateAsync(team.ProjectId, "Selected", null, "original",
            DocumentVisibility.PersonasSeleccionadas, new[] { reader.UserId, editor.UserId });
        var readable = await Service(db, reader).GetAsync(doc.Id);
        Assert.False(readable.CanEdit); Assert.False(readable.CanManageAccess);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, reader).UpdateAsync(doc.Id, doc.Title, "blocked", null, rowVersion: doc.RowVersion));
        Assert.Empty(await Service(db, outsider).ListAsync(team.ProjectId));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, outsider).GetAsync(doc.Id));
        var edited = await Service(db, editor).UpdateAsync(doc.Id, doc.Title, "edited", "Review complete", rowVersion: doc.RowVersion);
        Assert.True(edited.CanEdit); Assert.False(edited.CanManageAccess);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, editor).UpdateAsync(doc.Id, doc.Title, "edited", null,
            DocumentVisibility.PublicaProyecto, Array.Empty<Guid>(), rowVersion: edited.RowVersion));
        Assert.Equal(DocumentVisibility.PersonasSeleccionadas, (await Service(db, author).GetAsync(doc.Id)).Visibility);
        Assert.Contains(await Service(db, author).GetVersionsAsync(doc.Id), v => v.Content == "edited" && v.Note == "Review complete");
    }

    [Fact]
    public async Task Audience_changes_are_audited_and_immediately_protect_content_and_history()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var author = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var owner = Service(db, author);
        var doc = await owner.CreateAsync(team.ProjectId, "Visible before revocation", null, "Sensitive text", DocumentVisibility.PublicaProyecto);
        Assert.Single(await Service(db, reader).ListAsync(team.ProjectId));
        doc = await owner.UpdateAsync(doc.Id, doc.Title, doc.Content!, null, DocumentVisibility.PersonasSeleccionadas,
            new[] { reader.UserId }, rowVersion: doc.RowVersion);
        Assert.Single(await Service(db, reader).GetVersionsAsync(doc.Id));
        doc = await owner.UpdateAsync(doc.Id, doc.Title, doc.Content!, null, DocumentVisibility.Privada,
            Array.Empty<Guid>(), rowVersion: doc.RowVersion);
        Assert.Empty(await Service(db, reader).ListAsync(team.ProjectId));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, reader).GetAsync(doc.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, reader).GetVersionsAsync(doc.Id));
        var audits = await db.AuditLogs.Where(a => a.EntityType == "Document" && a.EntityId == doc.Id && a.Action == "ChangeAccess").ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => { Assert.Equal(author.UserId, a.ActorId); Assert.DoesNotContain("Sensitive text", a.NewState!); });
        Assert.False(await db.DocumentReaders.AnyAsync(r => r.DocumentId == doc.Id));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("organization")]
    [InlineData("user")]
    public async Task Revoked_membership_or_account_blocks_an_existing_author_service(string kind)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var author = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear);
        var service = Service(db, author);
        var doc = await service.CreateAsync(team.ProjectId, "Revocable", null, "Content");
        if (kind == "project") (await db.ProjectMembers.SingleAsync(m => m.ProjectId == team.ProjectId && m.UserId == author.UserId)).IsActive = false;
        else if (kind == "organization") (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == team.OrgId && m.UserId == author.UserId)).IsActive = false;
        else (await db.Users.SingleAsync(u => u.Id == author.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(team.ProjectId));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetAsync(doc.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetVersionsAsync(doc.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(doc.Id, doc.Title, "Must not save", null, rowVersion: doc.RowVersion));
        await using var verify = database.NewDb();
        Assert.Equal("Content", (await verify.Documents.SingleAsync(d => d.Id == doc.Id)).Content);
    }

    [Fact]
    public async Task Selected_audience_rejects_foreign_inactive_unreadable_and_empty_recipients()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var otherTeam = await SeedTeamAsync(db);
        var unreadable = await AddMemberAsync(db, team, Permission.Crear);
        var inactive = await AddMemberAsync(db, team, Permission.Leer);
        (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == team.OrgId && m.UserId == inactive.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        foreach (var readers in new[] { Array.Empty<Guid>(), new[] { otherTeam.UserId }, new[] { unreadable.UserId }, new[] { inactive.UserId } })
            await Assert.ThrowsAsync<ValidationException>(() => Service(db, team).CreateAsync(team.ProjectId, "Rejected audience", null, "No copy",
                DocumentVisibility.PersonasSeleccionadas, readers));
        Assert.False(await db.Documents.AnyAsync(d => d.ProjectId == team.ProjectId));
    }

    [Fact]
    public async Task Parent_metadata_is_hidden_without_parent_access_and_foreign_parent_creation_is_rejected()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear);
        var owner = Service(db, team);
        var parent = await owner.CreateAsync(team.ProjectId, "Secret parent", null, "Secret body");
        var child = await owner.CreateAsync(team.ProjectId, "Public child", parent.Id, "Public body", DocumentVisibility.PublicaProyecto);
        var metadata = Assert.Single(await Service(db, reader).ListAsync(team.ProjectId));
        Assert.Equal(child.Id, metadata.Id); Assert.Null(metadata.ParentId); Assert.Null(metadata.Content);
        Assert.Null((await Service(db, reader).GetAsync(child.Id)).ParentId);
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, reader).CreateAsync(team.ProjectId, "Unauthorized child", parent.Id, "Text"));
        var otherProject = new Project { OrganizationId = team.OrgId, Name = "Other project", Code = $"P{Guid.NewGuid():N}"[..12] };
        db.Projects.Add(otherProject); await db.SaveChangesAsync();
        var otherParent = await owner.CreateAsync(otherProject.Id, "Other parent", null, "Text");
        await Assert.ThrowsAsync<ValidationException>(() => owner.CreateAsync(team.ProjectId, "Wrong scope", otherParent.Id, "Text"));
    }

    [Fact]
    public async Task Concurrent_edits_keep_the_saved_content_and_do_not_append_failed_versions()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var service = Service(db, team);
        var doc = await service.CreateAsync(team.ProjectId, "Concurrency", null, "v1");
        await using var second = database.NewDb();
        var competing = Service(second, team);
        var stale = await competing.GetAsync(doc.Id);
        var saved = await service.UpdateAsync(doc.Id, doc.Title, "v2", "New revision", rowVersion: doc.RowVersion);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => competing.UpdateAsync(doc.Id, doc.Title, "lost draft", null, rowVersion: stale.RowVersion));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(doc.Id, doc.Title, "missing token", null));
        Assert.Equal("v2", (await service.GetAsync(doc.Id)).Content);
        Assert.Equal(2, (await service.GetVersionsAsync(doc.Id)).Count);
        Assert.NotEqual(doc.RowVersion, saved.RowVersion);
    }

    [Theory]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"html\",\"text\":\"<script>alert(1)</script>\"}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"image\",\"attrs\":{\"src\":\"https://example.invalid/image.png\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"attrs\":{\"style\":\"color:red\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":\"two\"}}]}")]
    [InlineData("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"Bad link\",\"marks\":[{\"type\":\"link\",\"attrs\":{\"href\":\"javascript:alert(1)\"}}]}]}]}")]
    [InlineData("{\"type\":\"doc\",\"type\":\"paragraph\"}")]
    public async Task Unsafe_or_malformed_rich_content_is_rejected_before_persistence(string content)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        await Assert.ThrowsAsync<ValidationException>(() => Service(db, team).CreateAsync(team.ProjectId, "Unsafe", null, content, contentFormat: "tiptap"));
        Assert.False(await db.Documents.AnyAsync(d => d.ProjectId == team.ProjectId));
        Assert.False(await db.DocumentVersions.AnyAsync(v => v.Document!.ProjectId == team.ProjectId));
    }

    [Fact]
    public async Task Plain_legacy_content_and_its_history_remain_plain_after_rich_upgrade()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var service = Service(db, team);
        var plain = "Literal <b>text</b> and <script>never execute</script>";
        var doc = await service.CreateAsync(team.ProjectId, "Legacy", null, plain);
        Assert.Equal("plain", doc.ContentFormat); Assert.Equal(plain, doc.Content);
        var upgraded = await service.UpdateAsync(doc.Id, doc.Title, Rich, "Upgrade format", contentFormat: "tiptap", rowVersion: doc.RowVersion);
        Assert.Equal("tiptap", upgraded.ContentFormat);
        var versions = await service.GetVersionsAsync(doc.Id);
        Assert.Equal(2, versions.Count);
        Assert.Contains(versions, v => v.ContentFormat == "plain" && v.Content == plain);
        Assert.Contains(versions, v => v.ContentFormat == "tiptap" && JsonNode.DeepEquals(JsonNode.Parse(Rich), JsonNode.Parse(v.Content)));
        Assert.Null(Assert.Single(await service.ListAsync(team.ProjectId)).Content);
    }

    [Fact]
    public async Task Historical_document_notification_body_and_count_follow_current_audience_without_admin_bypass()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var author = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var owner = Service(db, author);
        var doc = await owner.CreateAsync(team.ProjectId, "Historic public title", null, "Historic body", DocumentVisibility.PublicaProyecto);
        foreach (var recipient in new[] { reader.UserId, team.UserId })
            db.Notifications.Add(new Notification { OrganizationId = team.OrgId, UserId = recipient,
                EntityType = "Document", EntityId = doc.Id, Message = "Historic public title and private fragment", Route = $"/documents/{doc.Id}",
                DedupKey = $"docs-test:{doc.Id}:{recipient}", Event = NotificationEvent.Mencion });
        await db.SaveChangesAsync();
        var current = new TestUser(reader.UserId, team.OrgId);
        var readerNotifications = new NotificationService(db, current, new PermissionService(db, current));
        Assert.Single((await readerNotifications.ListAsync(false, 1, 20)).Items);
        Assert.Equal(1, await readerNotifications.UnreadCountAsync());
        await owner.UpdateAsync(doc.Id, doc.Title, doc.Content!, null, DocumentVisibility.Privada,
            Array.Empty<Guid>(), rowVersion: doc.RowVersion);
        var hidden = await readerNotifications.ListAsync(false, 1, 20);
        Assert.Empty(hidden.Items); Assert.Equal(0, hidden.Total); Assert.Equal(0, await readerNotifications.UnreadCountAsync());
        var administrator = new TestUser(team.UserId, team.OrgId);
        var adminNotifications = new NotificationService(db, administrator, new PermissionService(db, administrator));
        Assert.Empty((await adminNotifications.ListAsync(false, 1, 20)).Items);
        Assert.Equal(0, await adminNotifications.UnreadCountAsync());
    }

    private static DocumentService Service(AppDbContext db, Team team)
    {
        var user = new TestUser(team.UserId, team.OrgId);
        return new(db, user, new PermissionService(db, user));
    }

    private static async Task<Team> SeedTeamAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = "Document tests", Slug = $"docs-{suffix}" };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"docs-{suffix}@test.invalid", FullName = "Docs admin" };
        var role = new Role { OrganizationId = organization.Id, Name = "Administrator", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(organization); db.Users.Add(user); db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = organization.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        var project = new Project { OrganizationId = organization.Id, Name = "Documents project", Code = $"D{suffix}"[..12] };
        db.Projects.Add(project); await db.SaveChangesAsync();
        return new(organization.Id, project.Id, user.Id);
    }

    private static async Task<Team> AddMemberAsync(AppDbContext db, Team team, params Permission[] permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"docs-member-{suffix}@test.invalid", FullName = "Document member" };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Role-{suffix}", Permissions = permissions.ToList() };
        db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = team.OrgId, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true };
        db.OrganizationMembers.Add(member);
        db.ProjectMembers.Add(new() { ProjectId = team.ProjectId, OrganizationMemberId = member.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        await db.SaveChangesAsync();
        return team with { UserId = user.Id };
    }

    private sealed record Team(Guid OrgId, Guid ProjectId, Guid UserId);
    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    { public string Name => "SQL document test"; public bool IsAuthenticated => true; }
}
