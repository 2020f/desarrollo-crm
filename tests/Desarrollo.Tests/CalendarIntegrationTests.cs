using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Calendar;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

/// <summary>Calendar behavior against the same disposable, verified SQL Server fixture as task diagrams.</summary>
public sealed class CalendarIntegrationTests(SqlServerTestDatabase database)
    : IClassFixture<SqlServerTestDatabase>
{
    private const string SantoDomingo = "America/Santo_Domingo";
    private static readonly DateTime OctoberStart = Utc(2026, 10, 1, 4);
    private static readonly DateTime NovemberStart = Utc(2026, 11, 1, 4);
    private static readonly DateTime Day27 = new(2026, 10, 27);

    [Fact]
    public async Task Task_dates_follow_creation_rescheduling_and_clearing_without_calendar_copies()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, calendar) = Services(db, team);
        var task = await work.CreateAsync(new("Map task", WorkItemType.TareaTecnica, team.ProjectId,
            null, Priority.Alta, "One source record", Day27, null));

        var original = Assert.Single(await RangeAsync(calendar));
        Assert.Equal("WorkItem", original.SourceType);
        Assert.Equal(task.Id, original.SourceId);
        Assert.Equal("Due", original.DateKind);
        Assert.Equal(Day27, original.Start);
        Assert.Equal(Day27.AddDays(1), original.End);
        Assert.Equal($"/tasks/{task.Id}", original.SourceRoute);
        Assert.True(original.IsDerived);
        Assert.False(original.CanEdit);
        Assert.False(original.CanDelete);

        task = await work.UpdateAsync(task.Id, Update(task) with { DueDate = Day27.AddDays(2), Title = "Rescheduled map task" });
        var moved = Assert.Single(await RangeAsync(calendar));
        Assert.Equal(original.Id, moved.Id);
        Assert.Equal(Day27.AddDays(2), moved.Start);
        Assert.Contains("Rescheduled map task", moved.Title);
        Assert.Empty(await calendar.GetRangeAsync(Utc(2026, 10, 27, 4), Utc(2026, 10, 28, 4), SantoDomingo));

        await work.UpdateAsync(task.Id, Update(task) with { DueDate = null });
        Assert.Empty(await RangeAsync(calendar));
        Assert.False(await db.CalendarEvents.AnyAsync(x => x.OrganizationId == team.OrgId));
        Assert.Single(await db.WorkItems.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Existing_planning_sources_have_unique_stable_entries_and_inclusive_civil_periods()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var project = await db.Projects.SingleAsync(x => x.Id == team.ProjectId);
        project.StartDate = Day27.AddDays(-1); project.DueDate = Day27.AddDays(1);
        // IDs may coincide across tables. The calendar must still distinguish each resource kind.
        var sharedId = Guid.NewGuid();
        var task = RawTask(team, "Dated task", Day27);
        task.Id = sharedId; task.StartDate = Day27.AddDays(-1);
        db.WorkItems.Add(task);
        db.Epics.Add(new Epic { Id = sharedId, ProjectId = team.ProjectId, Title = "Epic", DueDate = Day27 });
        db.Cycles.Add(new Cycle { Id = sharedId, ProjectId = team.ProjectId, Name = "Cycle", StartDate = Day27.AddDays(-2), EndDate = Day27.AddDays(2) });
        db.Milestones.Add(new Milestone { Id = sharedId, ProjectId = team.ProjectId, Title = "Milestone", DueDate = Day27 });
        db.Releases.Add(new Release { Id = sharedId, ProjectId = team.ProjectId, Name = "Release", PlannedDate = Day27 });
        var manual = ManualEvent(team, "Independent meeting");
        manual.Id = sharedId;
        db.CalendarEvents.Add(manual);
        await db.SaveChangesAsync();

        var calendar = Services(db, team).Calendar;
        var items = await RangeAsync(calendar);
        Assert.Equal(7, items.Count);
        Assert.Equal(items.Count, items.Select(x => x.Id).Distinct().Count());
        Assert.Equal(items.Select(x => x.Id).Order(), (await RangeAsync(calendar)).Select(x => x.Id).Order());
        Assert.Equal(6, items.Count(x => x.IsDerived));
        Assert.Equal(sharedId, Assert.Single(items, x => !x.IsDerived).Id);
        Assert.Equal(new[] { "Cycle", "Epic", "Milestone", "Project", "Release", "WorkItem" },
            items.Where(x => x.IsDerived).Select(x => x.SourceType).Order().ToArray());

        var taskEntry = Assert.Single(items, x => x.SourceType == "WorkItem");
        Assert.Equal("Period", taskEntry.DateKind);
        Assert.Equal((Day27.AddDays(-1), Day27.AddDays(1)), (taskEntry.Start, taskEntry.End));
        var cycle = Assert.Single(items, x => x.SourceType == "Cycle");
        Assert.Equal("Period", cycle.DateKind);
        Assert.Equal((Day27.AddDays(-2), Day27.AddDays(3)), (cycle.Start, cycle.End));
        Assert.Equal("Planned", Assert.Single(items, x => x.SourceType == "Release").DateKind);
        Assert.All(items.Where(x => x.IsDerived), x => Assert.True(x.IsAllDay));
        Assert.Equal($"/projects/{team.ProjectId}/backlog", Assert.Single(items, x => x.SourceType == "Epic").SourceRoute);
        Assert.Single(await db.CalendarEvents.Where(x => x.OrganizationId == team.OrgId).ToListAsync());
    }

    [Fact]
    public async Task Santo_Domingo_UTC_query_preserves_day_27_and_excludes_the_next_day_boundary()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var previous = RawTask(team, "Previous day", Day27.AddDays(-1));
        var expected = RawTask(team, "Expected day", Day27);
        var next = RawTask(team, "Next day", Day27.AddDays(1));
        db.WorkItems.AddRange(previous, expected, next);
        var endsAtRangeStart = ManualEvent(team, "Ends at midnight");
        endsAtRangeStart.Start = Utc(2026, 10, 27, 3); endsAtRangeStart.End = Utc(2026, 10, 27, 4);
        var startsAtRangeEnd = ManualEvent(team, "Starts tomorrow");
        startsAtRangeEnd.Start = Utc(2026, 10, 28, 4); startsAtRangeEnd.End = Utc(2026, 10, 28, 5);
        db.CalendarEvents.AddRange(endsAtRangeStart, startsAtRangeEnd);
        await db.SaveChangesAsync();

        var calendar = Services(db, team).Calendar;
        var entry = Assert.Single(await calendar.GetRangeAsync(Utc(2026, 10, 27, 4), Utc(2026, 10, 28, 4), SantoDomingo));
        Assert.Equal(expected.Id, entry.SourceId);
        Assert.Equal(Day27, entry.Start);
        Assert.Equal(Day27.AddDays(1), entry.End);
        Assert.Equal(DateTimeKind.Unspecified, entry.Start.Kind);
        Assert.True(entry.IsAllDay);
        Assert.Equal(expected.Id, Assert.Single(await calendar.GetRangeAsync(
            Utc(2026, 10, 27, 4), Utc(2026, 10, 28, 4))).SourceId);
    }

    [Fact]
    public async Task Calendar_excludes_private_projects_other_organizations_and_other_users_personal_records()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var hiddenProject = await SeedProjectAsync(db, team);
        var foreignTeam = await SeedTeamAsync(db);
        var visible = RawTask(team, "Visible project task", Day27);
        var hidden = RawTask(hiddenProject, "Hidden project task", Day27);
        var foreign = RawTask(foreignTeam, "Other organization", Day27);
        var ownPersonal = RawTask(reader, "My personal task", Day27); ownPersonal.ProjectId = null;
        var otherPersonal = RawTask(team, "Someone else's personal task", Day27); otherPersonal.ProjectId = null;
        db.WorkItems.AddRange(visible, hidden, foreign, ownPersonal, otherPersonal);
        var visibleEvent = ManualEvent(team, "Visible project meeting");
        var hiddenEvent = ManualEvent(hiddenProject, "Hidden project meeting");
        var foreignEvent = ManualEvent(foreignTeam, "Foreign meeting");
        var ownEvent = ManualEvent(reader, "My personal meeting"); ownEvent.ProjectId = null;
        var otherEvent = ManualEvent(team, "Another personal meeting"); otherEvent.ProjectId = null;
        db.CalendarEvents.AddRange(visibleEvent, hiddenEvent, foreignEvent, ownEvent, otherEvent);
        await db.SaveChangesAsync();

        var entries = await RangeAsync(Services(db, reader).Calendar);
        Assert.Equal(4, entries.Count);
        Assert.Contains(entries, x => x.SourceId == visible.Id);
        Assert.Contains(entries, x => x.SourceId == ownPersonal.Id);
        Assert.Contains(entries, x => x.Id == visibleEvent.Id);
        Assert.Contains(entries, x => x.Id == ownEvent.Id);
        Assert.DoesNotContain(entries, x => x.SourceId == hidden.Id || x.SourceId == foreign.Id || x.SourceId == otherPersonal.Id);
        Assert.DoesNotContain(entries, x => x.Id == hiddenEvent.Id || x.Id == foreignEvent.Id || x.Id == otherEvent.Id);
        Assert.All(entries, x => Assert.DoesNotContain("Hidden", x.Title));
    }

    [Theory]
    [InlineData("project")]
    [InlineData("membership")]
    [InlineData("user")]
    public async Task Revoked_access_is_rechecked_for_an_existing_calendar_service(string revoked)
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        db.WorkItems.Add(RawTask(team, "Revocable task", Day27));
        db.CalendarEvents.Add(ManualEvent(team, "Revocable meeting"));
        await db.SaveChangesAsync();
        var calendar = Services(db, reader).Calendar;
        Assert.Equal(2, (await RangeAsync(calendar)).Count);
        if (revoked == "project")
            (await db.ProjectMembers.SingleAsync(x => x.ProjectId == team.ProjectId && x.UserId == reader.UserId)).IsActive = false;
        else if (revoked == "membership")
            (await db.OrganizationMembers.SingleAsync(x => x.OrganizationId == team.OrgId && x.UserId == reader.UserId)).IsActive = false;
        else
            (await db.Users.SingleAsync(x => x.Id == reader.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        if (revoked == "project") Assert.Empty(await RangeAsync(calendar));
        else await Assert.ThrowsAsync<ForbiddenException>(() => RangeAsync(calendar));
    }

    [Fact]
    public async Task Exact_linked_all_day_event_deduplicates_projection_but_a_linked_meeting_remains()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var task = RawTask(team, "Linked task", Day27);
        db.WorkItems.Add(task);
        var calendar = Services(db, team).Calendar;
        await db.SaveChangesAsync();
        var derived = Assert.Single(await RangeAsync(calendar));
        var linkedAllDay = ManualEvent(team, "Existing calendar representation");
        linkedAllDay.Type = CalendarEventType.Tarea; linkedAllDay.IsAllDay = true;
        linkedAllDay.Start = Day27; linkedAllDay.End = Day27.AddDays(1); linkedAllDay.LinkedWorkItemId = task.Id;
        var meeting = ManualEvent(team, "Discuss linked task"); meeting.LinkedWorkItemId = task.Id;
        db.CalendarEvents.AddRange(linkedAllDay, meeting);
        await db.SaveChangesAsync();

        var entries = await RangeAsync(calendar);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, x => x.Id == linkedAllDay.Id && !x.IsDerived);
        Assert.Contains(entries, x => x.Id == meeting.Id && !x.IsDerived);
        Assert.DoesNotContain(entries, x => x.Id == derived.Id);
        Assert.Equal(2, await db.CalendarEvents.CountAsync(x => x.OrganizationId == team.OrgId));
        Assert.Single(await db.WorkItems.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task A_manual_event_cannot_reveal_a_linked_source_from_an_unreadable_project()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var hiddenTeam = await SeedProjectAsync(db, team);
        var secret = RawTask(hiddenTeam, "Private source", Day27);
        db.WorkItems.Add(secret);
        var forged = ManualEvent(team, "Private content copied into an accessible project");
        forged.LinkedWorkItemId = secret.Id;
        db.CalendarEvents.Add(forged);
        await db.SaveChangesAsync();
        Assert.Empty(await RangeAsync(Services(db, reader).Calendar));
    }

    [Fact]
    public async Task Readers_cannot_create_update_or_delete_project_events_and_failures_preserve_data()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var original = ManualEvent(team, "Preserve original");
        db.CalendarEvents.Add(original);
        await db.SaveChangesAsync();
        var calendar = Services(db, reader).Calendar;
        var visible = Assert.Single(await RangeAsync(calendar));
        Assert.False(visible.CanEdit); Assert.False(visible.CanDelete);
        await Assert.ThrowsAsync<ForbiddenException>(() => calendar.CreateAsync(EventRequest(team)));
        await Assert.ThrowsAsync<ForbiddenException>(() => calendar.UpdateAsync(original.Id, EventRequest(team) with { Title = "Forbidden edit" }));
        await Assert.ThrowsAsync<ForbiddenException>(() => calendar.DeleteAsync(original.Id));
        await using var verify = database.NewDb();
        var saved = Assert.Single(await verify.CalendarEvents.Where(x => x.OrganizationId == team.OrgId).ToListAsync());
        Assert.Equal("Preserve original", saved.Title);
        Assert.Equal(original.ProjectId, saved.ProjectId);
    }

    [Fact]
    public async Task Manual_updates_cannot_move_an_event_to_another_organization_or_assign_an_unreadable_member()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var foreign = await SeedTeamAsync(db);
        var unreadable = await AddMemberAsync(db, team, Permission.Crear);
        var calendar = Services(db, team).Calendar;
        var original = await calendar.CreateAsync(EventRequest(team));
        await Assert.ThrowsAsync<ForbiddenException>(() => calendar.UpdateAsync(original.Id, EventRequest(team) with { ProjectId = foreign.ProjectId }));
        var assignment = await Assert.ThrowsAnyAsync<AppException>(() => calendar.UpdateAsync(original.Id,
            EventRequest(team) with { AssigneeId = unreadable.UserId }));
        Assert.Contains(assignment.StatusCode, new[] { 400, 403 });
        await using var verify = database.NewDb();
        var saved = await verify.CalendarEvents.SingleAsync(x => x.Id == original.Id);
        Assert.Equal(team.ProjectId, saved.ProjectId);
        Assert.Null(saved.AssigneeId);
    }

    [Fact]
    public async Task Personal_event_edits_require_the_creator_even_when_another_user_is_in_the_same_organization()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var ownCalendar = Services(db, reader).Calendar;
        var original = await ownCalendar.CreateAsync(EventRequest(reader) with { ProjectId = null });
        var otherMember = await AddMemberAsync(db, team, Permission.Leer);
        var otherCalendar = Services(db, otherMember).Calendar;
        Assert.DoesNotContain(await RangeAsync(otherCalendar), x => x.Id == original.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => otherCalendar.UpdateAsync(original.Id, EventRequest(otherMember) with { ProjectId = null }));
        await Assert.ThrowsAsync<ForbiddenException>(() => otherCalendar.DeleteAsync(original.Id));
        var edited = await ownCalendar.UpdateAsync(original.Id, EventRequest(reader) with { ProjectId = null, Title = "Owner update" });
        Assert.Equal("Owner update", edited.Title);
        Assert.True(edited.CanEdit); Assert.True(edited.CanDelete);
    }

    [Fact]
    public async Task Invalid_ranges_and_event_dates_are_rejected_without_writing_records()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var calendar = Services(db, team).Calendar;
        await Assert.ThrowsAsync<ValidationException>(() => calendar.GetRangeAsync(OctoberStart, OctoberStart, SantoDomingo));
        await Assert.ThrowsAsync<ValidationException>(() => calendar.GetRangeAsync(OctoberStart, OctoberStart.AddDays(367), SantoDomingo));
        await Assert.ThrowsAsync<ValidationException>(() => calendar.GetRangeAsync(OctoberStart, NovemberStart, "Invalid/Nowhere"));
        await Assert.ThrowsAsync<ValidationException>(() => calendar.CreateAsync(EventRequest(team) with { End = Utc(2026, 10, 27, 12) }));
        Assert.False(await db.CalendarEvents.AnyAsync(x => x.OrganizationId == team.OrgId));
    }

    [Fact]
    public async Task Pending_calendar_notification_content_and_count_disappear_after_access_revocation()
    {
        await using var db = database.NewDb();
        foreach (var revoked in new[] { "project", "membership" })
        {
            var team = await SeedTeamAsync(db);
            var reader = await AddMemberAsync(db, team, Permission.Leer);
            var calendarEvent = ManualEvent(team, "Restricted calendar title");
            calendarEvent.AssigneeId = reader.UserId;
            db.CalendarEvents.Add(calendarEvent);
            var privateProject = await SeedProjectAsync(db, team);
            var privateTask = RawTask(privateProject, "Unreadable linked source", Day27);
            db.WorkItems.Add(privateTask);
            var forgedEvent = ManualEvent(team, "Unreadable source copied into event");
            forgedEvent.LinkedWorkItemId = privateTask.Id;
            forgedEvent.AssigneeId = reader.UserId;
            db.CalendarEvents.Add(forgedEvent);
            db.Notifications.Add(new Notification
            {
                OrganizationId = team.OrgId, UserId = reader.UserId, Event = NotificationEvent.Asignacion,
                Message = "Restricted calendar title", EntityType = "CalendarEvent", EntityId = calendarEvent.Id,
                Route = "/calendar", DedupKey = Guid.NewGuid().ToString("N"),
            });
            db.Notifications.Add(new Notification
            {
                OrganizationId = team.OrgId, UserId = reader.UserId, Event = NotificationEvent.Asignacion,
                Message = "Unreadable source copied into event", EntityType = "CalendarEvent", EntityId = forgedEvent.Id,
                Route = "/calendar", DedupKey = Guid.NewGuid().ToString("N"),
            });
            await db.SaveChangesAsync();
            var current = new TestUser(reader.UserId, reader.OrgId);
            var notifications = new NotificationService(db, current, new PermissionService(db, current));
            Assert.Equal("Restricted calendar title", Assert.Single((await notifications.ListAsync(false, 1, 100)).Items).Message);
            Assert.Equal(1, await notifications.UnreadCountAsync());

            if (revoked == "project")
                (await db.ProjectMembers.SingleAsync(x => x.ProjectId == team.ProjectId && x.UserId == reader.UserId)).IsActive = false;
            else
                (await db.OrganizationMembers.SingleAsync(x => x.OrganizationId == team.OrgId && x.UserId == reader.UserId)).IsActive = false;
            await db.SaveChangesAsync();

            var hidden = await notifications.ListAsync(false, 1, 100);
            Assert.Empty(hidden.Items);
            Assert.Equal(0, hidden.Total);
            Assert.Equal(0, await notifications.UnreadCountAsync());
        }
    }

    private static Task<List<CalendarEventDto>> RangeAsync(CalendarService calendar) => calendar.GetRangeAsync(OctoberStart, NovemberStart, SantoDomingo);
    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private static (WorkItemService Work, CalendarService Calendar) Services(AppDbContext db, Team team)
    {
        var current = new TestUser(team.UserId, team.OrgId);
        var permissions = new PermissionService(db, current);
        return (new(db, current, permissions), new(db, current, permissions));
    }

    private static WorkItem RawTask(Team team, string title, DateTime? due) => new()
    {
        OrganizationId = team.OrgId, ProjectId = team.ProjectId, StateId = team.StateId,
        AuthorId = team.UserId, CreatedBy = team.UserId, Title = title, DueDate = due,
        Code = $"C{Guid.NewGuid():N}"[..14], Type = WorkItemType.TareaTecnica,
    };

    private static CalendarEvent ManualEvent(Team team, string title) => new()
    {
        OrganizationId = team.OrgId, ProjectId = team.ProjectId, CreatedBy = team.UserId,
        Title = title, Type = CalendarEventType.Reunion, Start = Utc(2026, 10, 27, 13),
        End = Utc(2026, 10, 27, 14), TimeZoneId = SantoDomingo,
    };

    private static CreateCalendarEventRequest EventRequest(Team team) => new(
        "Manual meeting", null, CalendarEventType.Reunion, false,
        Utc(2026, 10, 27, 13), Utc(2026, 10, 27, 14), SantoDomingo, team.ProjectId,
        null, null, null, null, Priority.Media, null, null);

    private static UpdateWorkItemRequest Update(WorkItemDetailDto x) => new(
        x.Title, x.Type, x.AssigneeId, x.Priority, x.Description, x.Objective, x.Context,
        x.StartDate, x.DueDate, x.EstimateHours, x.CycleId, x.EpicId, x.ReleaseId, x.StateId,
        x.AcceptanceCriteria, x.Labels, x.ReproSteps, x.ExpectedResult, x.ActualResult,
        x.Environment, x.AffectedVersion, x.Severity, x.ResearchQuestion, x.ResearchConclusion,
        x.ParentTaskId, x.Notes, x.ProgressPercent, x.AccentColor, RowVersion: x.RowVersion);

    private static async Task<Team> SeedTeamAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var organization = new Organization { Name = "Calendar tests", Slug = $"calendar-{suffix}", TimeZoneId = SantoDomingo };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"admin-{suffix}@test.invalid", FullName = "Calendar admin", TimeZoneId = SantoDomingo };
        var role = new Role { OrganizationId = organization.Id, Name = "Admin", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(organization); db.Users.Add(user); db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = organization.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        await db.SaveChangesAsync();
        return await SeedProjectAsync(db, new(organization.Id, user.Id, Guid.Empty, Guid.Empty));
    }

    private static async Task<Team> SeedProjectAsync(AppDbContext db, Team team)
    {
        var project = new Project { OrganizationId = team.OrgId, Name = "Calendar project", Code = $"C{Guid.NewGuid():N}"[..12], IsPrivate = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        await db.EnsureDefaultStatesAsync(project.Id, team.OrgId);
        await db.SaveChangesAsync();
        return team with { ProjectId = project.Id, StateId = await db.WorkflowStates.Where(x => x.ProjectId == project.Id && x.IsDefault).Select(x => x.Id).SingleAsync() };
    }

    private static async Task<Team> AddMemberAsync(AppDbContext db, Team team, params Permission[] permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"member-{suffix}@test.invalid", FullName = "Calendar member", TimeZoneId = SantoDomingo };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Role-{suffix}", Permissions = permissions.ToList() };
        db.Users.Add(user); db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync();
        var member = new OrganizationMember { OrganizationId = team.OrgId, UserId = user.Id, RoleId = role.Id, IsActive = true, Name = user.FullName };
        db.OrganizationMembers.Add(member);
        db.ProjectMembers.Add(new ProjectMember { ProjectId = team.ProjectId, OrganizationMemberId = member.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        await db.SaveChangesAsync();
        return team with { UserId = user.Id };
    }

    private sealed record Team(Guid OrgId, Guid UserId, Guid ProjectId, Guid StateId);
    private sealed record TestUser(Guid? UserId, Guid? OrganizationId) : ICurrentUser
    {
        public string Name => "SQL calendar test";
        public bool IsAuthenticated => true;
    }
}
