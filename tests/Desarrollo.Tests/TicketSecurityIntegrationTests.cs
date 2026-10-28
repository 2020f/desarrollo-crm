using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Files;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Tickets;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class TicketSecurityIntegrationTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    [Fact]
    public async Task Lists_counts_details_and_search_exclude_hidden_projects_and_foreign_organizations()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var reader = await MemberAsync(db, team, Permission.Leer);
        var hiddenProject = await ProjectAsync(db, team);
        var foreign = await SeedAsync(db);
        var visible = RawTicket(team, "Visible");
        var hidden = RawTicket(hiddenProject, "Hidden secret");
        var otherOrg = RawTicket(foreign, "Foreign secret");
        db.Tickets.AddRange(visible, hidden, otherOrg); await db.SaveChangesAsync();
        var service = Service(db, reader);
        var list = await service.ListAsync(Query());
        Assert.Equal(1, list.Total); Assert.Equal(visible.Id, Assert.Single(list.Items).Id);
        Assert.Equal(0, (await service.ListAsync(Query() with { Search = "secret" })).Total);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(hidden.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(otherOrg.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetCommentsAsync(hidden.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(Query() with { ProjectId = hiddenProject.ProjectId }));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SetStatusAsync(hidden.Id, TicketStatus.Cerrado, null));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AssignAsync(otherOrg.Id, reader.UserId));
        await using var verify = database.NewDb();
        Assert.Equal(TicketStatus.Recibido, (await verify.Tickets.SingleAsync(t => t.Id == hidden.Id)).Status);
    }

    [Fact]
    public async Task Readers_cannot_mutate_tickets_and_do_not_receive_internal_notes_or_flags()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var reader = await MemberAsync(db, team, Permission.Leer);
        var ticket = RawTicket(team, "Read only"); db.Tickets.Add(ticket); await db.SaveChangesAsync();
        var owner = Service(db, team);
        await owner.AddCommentAsync(ticket.Id, "Shared answer", false);
        await owner.AddCommentAsync(ticket.Id, "Internal evidence", true);
        var service = Service(db, reader);
        var dto = await service.GetAsync(ticket.Id);
        Assert.False(dto.CanAssign); Assert.False(dto.CanChangeStatus); Assert.False(dto.CanComment);
        Assert.False(dto.CanReadInternalNotes); Assert.False(dto.CanWriteInternalNotes); Assert.False(dto.CanLinkWorkItems);
        Assert.Equal("Shared answer", Assert.Single(await service.GetCommentsAsync(ticket.Id)).Body);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(Request(team)));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SetStatusAsync(ticket.Id, TicketStatus.Resuelto, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AssignAsync(ticket.Id, null));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AddCommentAsync(ticket.Id, "Not allowed", false));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.LinkWorkItemAsync(ticket.Id, Guid.NewGuid()));
        Assert.Equal(2, await db.TicketComments.CountAsync(c => c.TicketId == ticket.Id));
    }

    [Fact]
    public async Task Guest_can_post_shared_replies_but_only_editors_can_read_or_write_internal_notes()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var guest = await MemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var editor = await MemberAsync(db, team, Permission.Leer, Permission.Comentar, Permission.Editar);
        var ticket = RawTicket(team, "Conversation"); db.Tickets.Add(ticket); await db.SaveChangesAsync();
        var publicComment = await Service(db, guest).AddCommentAsync(ticket.Id, "  Customer answer  ", false);
        Assert.Equal("Customer answer", publicComment.Body);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, guest).AddCommentAsync(ticket.Id, "Hidden attempt", true));
        var internalComment = await Service(db, editor).AddCommentAsync(ticket.Id, "Private staff note", true);
        Assert.True(internalComment.IsInternal);
        Assert.Single(await Service(db, guest).GetCommentsAsync(ticket.Id));
        Assert.Equal(2, (await Service(db, editor).GetCommentsAsync(ticket.Id)).Count);
        var staffDto = await Service(db, editor).GetAsync(ticket.Id);
        Assert.True(staffDto.CanReadInternalNotes); Assert.True(staffDto.CanWriteInternalNotes);
        Assert.False(staffDto.CanAssign); Assert.False(staffDto.CanChangeStatus);
    }

    [Fact]
    public async Task Create_assign_and_change_state_capabilities_are_independent()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var creator = await MemberAsync(db, team, Permission.Leer, Permission.Crear);
        var assigner = await MemberAsync(db, team, Permission.Leer, Permission.Asignar);
        var transitioner = await MemberAsync(db, team, Permission.Leer, Permission.CambiarEstado);
        var created = await Service(db, creator).CreateAsync(Request(team));
        Assert.False(created.CanAssign); Assert.False(created.CanChangeStatus);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, creator).CreateAsync(Request(team) with { AssigneeId = assigner.UserId }));
        var assigned = await Service(db, assigner).AssignAsync(created.Id, creator.UserId);
        Assert.Equal(creator.UserId, assigned.AssigneeId);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, assigner).SetStatusAsync(created.Id, TicketStatus.EnEvaluacion, null));
        var changed = await Service(db, transitioner).SetStatusAsync(created.Id, TicketStatus.EnEvaluacion, "Initial review");
        Assert.Equal(TicketStatus.EnEvaluacion, changed.Status);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, transitioner).AssignAsync(created.Id, null));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("membership")]
    [InlineData("user")]
    public async Task Revocation_blocks_existing_ticket_services_and_does_not_change_records(string kind)
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var member = await MemberAsync(db, team, Permission.Leer, Permission.Comentar, Permission.Crear, Permission.Editar, Permission.Asignar, Permission.CambiarEstado);
        var ticket = RawTicket(team, "Revocable"); db.Tickets.Add(ticket); await db.SaveChangesAsync();
        var service = Service(db, member);
        Assert.Equal(ticket.Id, (await service.GetAsync(ticket.Id)).Id);
        if (kind == "project") (await db.ProjectMembers.SingleAsync(m => m.ProjectId == team.ProjectId && m.UserId == member.UserId)).IsActive = false;
        else if (kind == "membership") (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == team.OrgId && m.UserId == member.UserId)).IsActive = false;
        else (await db.Users.SingleAsync(u => u.Id == member.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        if (kind == "project") Assert.Empty((await service.ListAsync(Query())).Items);
        else await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(Query()));
        await Assert.ThrowsAnyAsync<AppException>(() => service.GetAsync(ticket.Id));
        await Assert.ThrowsAnyAsync<AppException>(() => service.GetCommentsAsync(ticket.Id));
        await Assert.ThrowsAnyAsync<AppException>(() => service.SetStatusAsync(ticket.Id, TicketStatus.Cerrado, null));
        await Assert.ThrowsAnyAsync<AppException>(() => service.AssignAsync(ticket.Id, team.UserId));
        await Assert.ThrowsAnyAsync<AppException>(() => service.AddCommentAsync(ticket.Id, "Must not save", false));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(Request(team)));
        await using var verify = database.NewDb();
        Assert.Equal(TicketStatus.Recibido, (await verify.Tickets.SingleAsync(t => t.Id == ticket.Id)).Status);
        Assert.False(await verify.TicketComments.AnyAsync(c => c.TicketId == ticket.Id));
    }

    [Fact]
    public async Task Personal_tickets_are_visible_only_to_requester_or_assignee_even_for_admins()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var requester = await MemberAsync(db, team, Permission.Leer, Permission.Comentar, Permission.Crear);
        var assignee = await MemberAsync(db, team, Permission.Leer);
        var stranger = await MemberAsync(db, team, Permission.Leer);
        var personal = RawTicket(requester, "Personal request"); personal.ProjectId = null; personal.AssigneeId = assignee.UserId;
        db.Tickets.Add(personal); await db.SaveChangesAsync();
        Assert.Single((await Service(db, requester).ListAsync(Query())).Items);
        Assert.Single((await Service(db, assignee).ListAsync(Query())).Items);
        Assert.Empty((await Service(db, stranger).ListAsync(Query())).Items);
        Assert.Empty((await Service(db, team).ListAsync(Query())).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, team).GetAsync(personal.Id));
        var created = await Service(db, requester).CreateAsync(Request(team) with { ProjectId = null });
        Assert.Null(created.ProjectId); Assert.Equal(requester.UserId, created.RequesterId);
        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db, assignee).CreateAsync(Request(team) with { ProjectId = null }));
    }

    [Fact]
    public async Task Assignment_rejects_foreign_inactive_and_project_unreadable_recipients()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var foreign = await SeedAsync(db);
        var unreadable = await MemberAsync(db, team, Permission.Crear);
        var inactive = await MemberAsync(db, team, Permission.Leer);
        (await db.OrganizationMembers.SingleAsync(m => m.OrganizationId == team.OrgId && m.UserId == inactive.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        var service = Service(db, team);
        var ticket = await service.CreateAsync(Request(team));
        foreach (var candidate in new[] { foreign.UserId, unreadable.UserId, inactive.UserId, Guid.NewGuid() })
            await Assert.ThrowsAsync<ValidationException>(() => service.AssignAsync(ticket.Id, candidate));
        Assert.Null((await service.GetAsync(ticket.Id)).AssigneeId);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(Request(team) with { ProjectId = foreign.ProjectId }));
    }

    [Fact]
    public async Task Work_links_require_edit_and_matching_readable_project_and_are_idempotent()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var otherProject = await ProjectAsync(db, team);
        var foreign = await SeedAsync(db);
        var task = RawWork(team); var wrongProject = RawWork(otherProject); var wrongOrg = RawWork(foreign);
        db.WorkItems.AddRange(task, wrongProject, wrongOrg); await db.SaveChangesAsync();
        var service = Service(db, team);
        var ticket = await service.CreateAsync(Request(team));
        await service.LinkWorkItemAsync(ticket.Id, task.Id); await service.LinkWorkItemAsync(ticket.Id, task.Id);
        await Assert.ThrowsAsync<ValidationException>(() => service.LinkWorkItemAsync(ticket.Id, wrongProject.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.LinkWorkItemAsync(ticket.Id, wrongOrg.Id));
        Assert.Single(await db.TicketWorkItemLinks.Where(l => l.TicketId == ticket.Id).ToListAsync());
    }

    [Fact]
    public async Task Invalid_enums_pagination_products_and_empty_comments_do_not_write_ticket_data()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var foreign = await SeedAsync(db);
        var product = new Product { OrganizationId = foreign.OrgId, Name = "Foreign product" }; db.Products.Add(product); await db.SaveChangesAsync();
        var service = Service(db, team);
        await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync(Query() with { Page = 0 }));
        await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync(Query() with { PageSize = 101 }));
        await Assert.ThrowsAsync<ValidationException>(() => service.ListAsync(Query() with { Status = (TicketStatus)99 }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(team) with { Category = (TicketCategory)99 }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(team) with { Priority = (Priority)99 }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(Request(team) with { ProductId = product.Id }));
        Assert.False(await db.Tickets.AnyAsync(t => t.OrganizationId == team.OrgId));
        var ticket = await service.CreateAsync(Request(team));
        await Assert.ThrowsAsync<ValidationException>(() => service.SetStatusAsync(ticket.Id, (TicketStatus)99, null));
        await Assert.ThrowsAsync<ValidationException>(() => service.AddCommentAsync(ticket.Id, "   ", false));
        Assert.False(await db.TicketComments.AnyAsync(c => c.TicketId == ticket.Id));
    }

    [Fact]
    public async Task Ticket_and_internal_comment_notifications_filter_body_and_count_then_follow_revocation()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var guest = await MemberAsync(db, team, Permission.Leer, Permission.Comentar);
        var hiddenProject = await ProjectAsync(db, team);
        var visible = RawTicket(team, "Visible notification"); var hidden = RawTicket(hiddenProject, "Hidden notification");
        db.Tickets.AddRange(visible, hidden); await db.SaveChangesAsync();
        var note = new TicketComment { TicketId = visible.Id, AuthorId = team.UserId, Body = "Internal private note", IsInternal = true };
        db.TicketComments.Add(note); await db.SaveChangesAsync();
        foreach (var (type, id, message) in new[] { ("Ticket", visible.Id, "Visible notification"), ("Ticket", hidden.Id, "Hidden notification"), ("TicketComment", note.Id, "Internal private note") })
            db.Notifications.Add(new Notification { OrganizationId = team.OrgId, UserId = guest.UserId, EntityType = type, EntityId = id,
                Message = message, Event = NotificationEvent.Asignacion, DedupKey = $"ticket-security:{Guid.NewGuid():N}" });
        await db.SaveChangesAsync();
        var user = new TestUser(guest.UserId, team.OrgId);
        var notifications = new NotificationService(db, user, new PermissionService(db, user));
        var initial = await notifications.ListAsync(false, 1, 50);
        Assert.Equal(1, initial.Total); Assert.Equal("Visible notification", Assert.Single(initial.Items).Message);
        Assert.Equal(1, await notifications.UnreadCountAsync());
        (await db.ProjectMembers.SingleAsync(m => m.ProjectId == team.ProjectId && m.UserId == guest.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty((await notifications.ListAsync(false, 1, 50)).Items); Assert.Equal(0, await notifications.UnreadCountAsync());
    }

    [Fact]
    public async Task Legacy_ticket_attachment_lists_counts_and_guessed_download_ids_use_the_same_ticket_policy()
    {
        await using var db = database.NewDb();
        var team = await SeedAsync(db);
        var reader = await MemberAsync(db, team, Permission.Leer);
        var hiddenProject = await ProjectAsync(db, team);
        var visible = RawTicket(team, "Visible attachment"); var hidden = RawTicket(hiddenProject, "Hidden attachment");
        db.Tickets.AddRange(visible, hidden); await db.SaveChangesAsync();
        var shown = RawFile(team); var secret = RawFile(hiddenProject);
        db.StoredFiles.AddRange(shown, secret);
        db.Attachments.AddRange(new Attachment { OrganizationId = team.OrgId, StoredFileId = shown.Id, EntityType = "Ticket", EntityId = visible.Id },
            new Attachment { OrganizationId = team.OrgId, StoredFileId = secret.Id, EntityType = "Ticket", EntityId = hidden.Id });
        await db.SaveChangesAsync();
        var user = new TestUser(reader.UserId, team.OrgId); var permissions = new PermissionService(db, user);
        var files = new FileService(db, user, permissions, new LocalPrivateFileStorage(Path.GetTempPath()), new DocumentService(db, user, permissions));
        var listed = await files.ListAsync(new(null, FileVisibility.Context, null, 1, 50));
        Assert.Equal(1, listed.Total); Assert.Equal(shown.Id, Assert.Single(listed.Items).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => files.OpenAsync(secret.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => files.ListAsync(new(null, FileVisibility.Context, null, 1, 50, "Ticket", hidden.Id)));
    }

    [Fact]
    public async Task Concurrent_creation_uses_unique_organization_numbers_without_overwriting_records()
    {
        await using var seed = database.NewDb(); var team = await SeedAsync(seed);
        await using var first = database.NewDb(); await using var second = database.NewDb();
        var created = await Task.WhenAll(Service(first, team).CreateAsync(Request(team)), Service(second, team).CreateAsync(Request(team)));
        Assert.Equal(2, created.Select(t => t.Number).Distinct().Count());
        Assert.Equal(2, created.Select(t => t.Code).Distinct().Count());
        Assert.Equal(2, await seed.Tickets.CountAsync(t => t.OrganizationId == team.OrgId));
    }

    private static TicketQuery Query() => new(null, null, null);
    private static CreateTicketRequest Request(Team team) => new("Ticket test", TicketCategory.Error, "Description", "Reproduce", null,
        team.ProjectId, Severity.Media, Priority.Media, null, null);
    private static Ticket RawTicket(Team team, string title) => new() { OrganizationId = team.OrgId, ProjectId = team.ProjectId,
        RequesterId = team.UserId, Title = title, Number = 1, Code = $"T{Guid.NewGuid():N}"[..14], CreatedBy = team.UserId };
    private static WorkItem RawWork(Team team) => new() { OrganizationId = team.OrgId, ProjectId = team.ProjectId,
        StateId = team.StateId, AuthorId = team.UserId, Title = "Linkable task", Code = $"W{Guid.NewGuid():N}"[..14] };
    private static StoredFile RawFile(Team team) => new() { OrganizationId = team.OrgId, ProjectId = team.ProjectId,
        Visibility = FileVisibility.Context, UploaderId = team.UserId, OriginalName = "evidence.txt", InternalName = $"{Guid.NewGuid():N}.txt", ContentType = "text/plain", SizeBytes = 1 };
    private static TicketService Service(AppDbContext db, Team team)
    { var user = new TestUser(team.UserId, team.OrgId); return new(db, user, new PermissionService(db, user)); }
    private static async Task<Team> SeedAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var org = new Organization { Name = "Ticket security", Slug = $"ticket-{suffix}" };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"ticket-{suffix}@test.invalid", FullName = "Ticket administrator" };
        var role = new Role { OrganizationId = org.Id, Name = "Admin", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(org); db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = org.Id, UserId = user.Id, RoleId = role.Id, IsActive = true, Name = user.FullName });
        await db.SaveChangesAsync(); return await ProjectAsync(db, new(org.Id, Guid.Empty, Guid.Empty, user.Id));
    }
    private static async Task<Team> ProjectAsync(AppDbContext db, Team team)
    {
        var project = new Project { OrganizationId = team.OrgId, Name = "Ticket project", Code = $"T{Guid.NewGuid():N}"[..12] };
        db.Projects.Add(project); await db.SaveChangesAsync(); await db.EnsureDefaultStatesAsync(project.Id, team.OrgId); await db.SaveChangesAsync();
        return team with { ProjectId = project.Id, StateId = await db.WorkflowStates.Where(s => s.ProjectId == project.Id && s.IsDefault).Select(s => s.Id).SingleAsync() };
    }
    private static async Task<Team> MemberAsync(AppDbContext db, Team team, params Permission[] permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"ticket-member-{suffix}@test.invalid", FullName = "Ticket member" };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Role-{suffix}", Permissions = permissions.ToList() };
        db.Users.Add(user); db.OrganizationRoles.Add(role); await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = team.OrgId, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true };
        db.OrganizationMembers.Add(member); db.ProjectMembers.Add(new() { ProjectId = team.ProjectId, OrganizationMemberId = member.Id, UserId = user.Id, RoleId = role.Id, IsActive = true, Name = user.FullName });
        await db.SaveChangesAsync(); return team with { UserId = user.Id };
    }
    private sealed record Team(Guid OrgId, Guid ProjectId, Guid StateId, Guid UserId);
    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    { public string Name => "SQL ticket security test"; public bool IsAuthenticated => true; }
}
