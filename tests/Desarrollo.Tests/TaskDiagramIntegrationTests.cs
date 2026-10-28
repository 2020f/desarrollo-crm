using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Tests;

public sealed class TaskDiagramIntegrationTests(SqlServerTestDatabase database)
    : IClassFixture<SqlServerTestDatabase>
{
    [Fact]
    public async Task Organization_admin_cannot_read_or_mutate_another_organizations_map()
    {
        await using var db = database.NewDb();
        var a = await SeedTeamAsync(db);
        var b = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, a);

        await Assert.ThrowsAsync<ForbiddenException>(() => diagram.GetAsync(b.ProjectId));
        await Assert.ThrowsAsync<ForbiddenException>(() => diagram.SaveViewportAsync(b.ProjectId, new(0, 0, 1)));
        await Assert.ThrowsAsync<ForbiddenException>(() => diagram.SavePositionsAsync(b.ProjectId, []));
        await Assert.ThrowsAsync<ForbiddenException>(() => work.CreateAsync(Request(b, "Forbidden")));
        Assert.False(await db.WorkItems.AnyAsync(x => x.ProjectId == b.ProjectId));
    }

    [Fact]
    public async Task Position_batch_with_foreign_task_is_rejected_without_partial_moves()
    {
        await using var db = database.NewDb();
        var a = await SeedTeamAsync(db);
        var b = await SeedTeamAsync(db);
        var (workA, mapA) = Services(db, a);
        var (workB, mapB) = Services(db, b);
        var taskA = await workA.CreateAsync(Request(a));
        var taskB = await workB.CreateAsync(Request(b));
        var posA = Assert.Single((await mapA.GetAsync(a.ProjectId)).Tasks);
        var posB = Assert.Single((await mapB.GetAsync(b.ProjectId)).Tasks);

        await Assert.ThrowsAnyAsync<AppException>(() => mapA.SavePositionsAsync(a.ProjectId,
        [new(taskA.Id, 99, 44, posA.PositionRowVersion), new(taskB.Id, 123, 77, posB.PositionRowVersion)]));

        await using var verify = database.NewDb();
        var savedA = await verify.TaskDiagramPositions.SingleAsync(x => x.TaskId == taskA.Id);
        var savedB = await verify.TaskDiagramPositions.SingleAsync(x => x.TaskId == taskB.Id);
        Assert.Equal((posA.X, posA.Y), (savedA.X, savedA.Y));
        Assert.Equal((posB.X, posB.Y), (savedB.X, savedB.Y));
        Assert.Equal(b.ProjectId, savedB.ProjectId);
    }

    [Fact]
    public async Task Generic_creation_cannot_use_parent_from_another_project()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var other = await SeedProjectAsync(db, team);
        var (work, _) = Services(db, team);
        var parent = await work.CreateAsync(Request(other));

        await Assert.ThrowsAnyAsync<AppException>(() => work.CreateAsync(Request(team) with { ParentTaskId = parent.Id }));
        Assert.False(await db.WorkItems.AnyAsync(x => x.ProjectId == team.ProjectId));
    }

    [Fact]
    public async Task Reparenting_rejects_self_and_descendant_but_allows_ancestor_and_detach()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, map) = Services(db, team);
        var root = await work.CreateAsync(Request(team, "Root"));
        var child = await work.CreateChildAsync(root.Id, Request(team, "Child"));
        var grandchild = await work.CreateChildAsync(child.Id, Request(team, "Grandchild"));
        root = await work.GetAsync(root.Id);

        await Assert.ThrowsAnyAsync<AppException>(() => work.ChangeParentAsync(root.Id, root.Id, root.RowVersion));
        await Assert.ThrowsAnyAsync<AppException>(() => work.ChangeParentAsync(root.Id, grandchild.Id, root.RowVersion));
        var moved = await work.ChangeParentAsync(grandchild.Id, root.Id, grandchild.RowVersion);
        Assert.Equal(root.Id, moved.ParentTaskId);
        var detached = await work.ChangeParentAsync(moved.Id, null, moved.RowVersion);
        Assert.Null(detached.ParentTaskId);
        var tasks = (await map.GetAsync(team.ProjectId)).Tasks;
        Assert.Equal(0, tasks.Single(x => x.Id == detached.Id).Depth);
        Assert.Equal(1, tasks.Single(x => x.Id == child.Id).Depth);
    }

    [Fact]
    public async Task Generic_update_cannot_introduce_a_parent_cycle()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, _) = Services(db, team);
        var root = await work.CreateAsync(Request(team));
        var child = await work.CreateChildAsync(root.Id, Request(team));
        root = await work.GetAsync(root.Id);

        await Assert.ThrowsAnyAsync<AppException>(() => work.UpdateAsync(root.Id, Update(root) with { ParentTaskId = child.Id }));
        await using var verify = database.NewDb();
        Assert.Null((await verify.WorkItems.SingleAsync(x => x.Id == root.Id)).ParentTaskId);
    }

    [Fact]
    public async Task Reparenting_cannot_move_task_to_another_project()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var other = await SeedProjectAsync(db, team);
        var (work, _) = Services(db, team);
        var task = await work.CreateAsync(Request(team));
        var foreignParent = await work.CreateAsync(Request(other));
        await Assert.ThrowsAnyAsync<AppException>(() => work.ChangeParentAsync(task.Id, foreignParent.Id, task.RowVersion));
    }

    [Fact]
    public async Task Simultaneous_opposing_parent_changes_cannot_commit_a_cycle()
    {
        await using var setup = database.NewDb();
        var team = await SeedTeamAsync(setup);
        var work = Services(setup, team).Work;
        var a = await work.CreateAsync(Request(team, "A"));
        var b = await work.CreateAsync(Request(team, "B"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Attempt(WorkItemDetailDto child, Guid parentId)
        {
            await using var db = database.NewDb();
            await start.Task;
            try
            {
                await Services(db, team).Work.ChangeParentAsync(child.Id, parentId, child.RowVersion);
                return true;
            }
            catch (AppException error) when (error.StatusCode is 400 or 409) { return false; }
        }
        var first = Attempt(a, b.Id);
        var second = Attempt(b, a.Id);
        start.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Single(results, x => x);
        Assert.Single(results, x => !x);
        await using var verify = database.NewDb();
        var parents = await verify.WorkItems.Where(x => x.ProjectId == team.ProjectId).Select(x => x.ParentTaskId).ToListAsync();
        Assert.Single(parents, x => x is null);
        Assert.Single(parents, x => x is not null);
    }

    [Fact]
    public async Task Stale_task_edit_preserves_the_newer_title_and_requires_a_version()
    {
        await using var setup = database.NewDb();
        var team = await SeedTeamAsync(setup);
        var task = await Services(setup, team).Work.CreateAsync(Request(team, "Original"));
        await using (var editor = database.NewDb())
        {
            var latest = await Services(editor, team).Work.PatchAsync(task.Id, new(Title: "Saved by A", RowVersion: task.RowVersion));
            Assert.NotEqual(task.RowVersion, latest.RowVersion);
        }
        await using (var stale = database.NewDb())
        {
            var error = await Assert.ThrowsAnyAsync<AppException>(() => Services(stale, team).Work.PatchAsync(
                task.Id, new(Title: "Stale B", RowVersion: task.RowVersion)));
            Assert.Equal(409, error.StatusCode);
        }
        await using (var missing = database.NewDb())
            await Assert.ThrowsAnyAsync<AppException>(() => Services(missing, team).Work.PatchAsync(task.Id, new(Title: "No version")));
        await using var verify = database.NewDb();
        Assert.Equal("Saved by A", (await verify.WorkItems.SingleAsync(x => x.Id == task.Id)).Title);
    }

    [Fact]
    public async Task Stale_position_batch_preserves_all_newer_coordinates()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, team);
        var first = await work.CreateAsync(Request(team));
        var second = await work.CreateAsync(Request(team));
        var initial = (await diagram.GetAsync(team.ProjectId)).Tasks;
        var a = initial.Single(x => x.Id == first.Id);
        var b = initial.Single(x => x.Id == second.Id);
        var saved = await diagram.SavePositionsAsync(team.ProjectId, [new(first.Id, 200, 300, a.PositionRowVersion)]);
        Assert.NotEqual(a.PositionRowVersion, Assert.Single(saved).RowVersion);

        await using (var stale = database.NewDb())
        {
            var error = await Assert.ThrowsAnyAsync<AppException>(() => Services(stale, team).Diagram.SavePositionsAsync(
                team.ProjectId, [new(second.Id, 900, 800, b.PositionRowVersion), new(first.Id, 0, 0, a.PositionRowVersion)]));
            Assert.Equal(409, error.StatusCode);
        }
        await using var verify = database.NewDb();
        var currentA = await verify.TaskDiagramPositions.SingleAsync(x => x.TaskId == first.Id);
        var currentB = await verify.TaskDiagramPositions.SingleAsync(x => x.TaskId == second.Id);
        Assert.Equal((200d, 300d), (currentA.X, currentA.Y));
        Assert.Equal((b.X, b.Y), (currentB.X, currentB.Y));
    }

    [Fact]
    public async Task Missing_position_version_cannot_overwrite_existing_layout()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, team);
        var task = await work.CreateAsync(Request(team));
        var initial = Assert.Single((await diagram.GetAsync(team.ProjectId)).Tasks);
        await Assert.ThrowsAnyAsync<AppException>(() => diagram.SavePositionsAsync(team.ProjectId, [new(task.Id, 800, 900)]));
        await using var verify = database.NewDb();
        var saved = await verify.TaskDiagramPositions.SingleAsync(x => x.TaskId == task.Id);
        Assert.Equal((initial.X, initial.Y), (saved.X, saved.Y));
    }

    [Fact]
    public async Task Task_creation_persists_state_category_and_non_overlapping_initial_positions()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, team);
        var root = await work.CreateAsync(Request(team, "Root"));
        var child = await work.CreateChildAsync(root.Id, Request(team, "Child"));
        var sibling = await work.CreateChildAsync(root.Id, Request(team, "Sibling"));
        var map = await diagram.GetAsync(team.ProjectId);
        Assert.Equal(3, map.Tasks.Count);
        Assert.Equal(3, await db.TaskDiagramPositions.CountAsync(x => x.ProjectId == team.ProjectId));
        Assert.All(map.Tasks, x => { Assert.False(string.IsNullOrEmpty(x.RowVersion)); Assert.False(string.IsNullOrEmpty(x.PositionRowVersion)); });
        Assert.Equal(3, map.Tasks.Select(x => (x.X, x.Y)).Distinct().Count());
        Assert.Contains("Root", map.Tasks.Single(x => x.Id == child.Id).FullPath);
        Assert.Equal(1, map.Tasks.Single(x => x.Id == sibling.Id).Depth);
        Assert.Equal(team.StateId, root.StateId);
        Assert.Equal(WorkItemCategory.Pendiente, root.Category);
    }

    [Fact]
    public async Task Creation_retry_with_same_request_id_creates_one_task_and_one_position()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, _) = Services(db, team);
        var request = Request(team) with { ClientRequestId = Guid.NewGuid() };
        var first = await work.CreateAsync(request);
        var retried = await work.CreateAsync(request);
        Assert.Equal(first.Id, retried.Id);
        Assert.Single(await db.WorkItems.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
        Assert.Single(await db.TaskDiagramPositions.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
        Assert.Equal(2, (await db.Projects.SingleAsync(x => x.Id == team.ProjectId)).WorkItemSequence);
    }

    [Fact]
    public async Task Failed_position_insert_rolls_back_child_and_project_sequence()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var parent = await Services(db, team).Work.CreateAsync(Request(team));
        var sequenceBefore = (await db.Projects.SingleAsync(x => x.Id == team.ProjectId)).WorkItemSequence;
        var triggerName = $"RejectDiagramPosition_{Guid.NewGuid():N}";
        // Generated GUIDs only; this trigger exists exclusively in the fixture's disposable SQL database.
#pragma warning disable EF1002 // SQL identifiers/predicate contain only generated GUID values, never external input.
        await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER [{triggerName}] ON [TaskDiagramPositions] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE ProjectId = '{team.ProjectId:D}') THROW 51000, 'Intentional position failure', 1; END");
        try
        {
            await using var mutation = database.NewDb();
            await Assert.ThrowsAsync<DbUpdateException>(() => Services(mutation, team).Work.CreateChildAsync(parent.Id, Request(team, "Must roll back")));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER IF EXISTS [{triggerName}]");
        }
#pragma warning restore EF1002
        await using var verify = database.NewDb();
        Assert.False(await verify.WorkItems.AnyAsync(x => x.ProjectId == team.ProjectId && x.ParentTaskId == parent.Id));
        Assert.Equal(sequenceBefore, (await verify.Projects.SingleAsync(x => x.Id == team.ProjectId)).WorkItemSequence);
        Assert.Single(await verify.TaskDiagramPositions.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Foreign_workflow_state_is_rejected_by_create_patch_update_and_transition()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var other = await SeedProjectAsync(db, team);
        var (work, _) = Services(db, team);
        var task = await work.CreateAsync(Request(team));
        await Assert.ThrowsAnyAsync<AppException>(() => work.CreateAsync(Request(team) with { StateId = other.StateId }));
        await Assert.ThrowsAnyAsync<AppException>(() => work.PatchAsync(task.Id, new(StateId: other.StateId, RowVersion: task.RowVersion)));
        await Assert.ThrowsAnyAsync<AppException>(() => work.UpdateAsync(task.Id, Update(task) with { StateId = other.StateId }));
        await Assert.ThrowsAnyAsync<AppException>(() => work.TransitionAsync(task.Id, other.StateId, "Invalid target", task.RowVersion));
        await using var verify = database.NewDb();
        Assert.Equal(team.StateId, (await verify.WorkItems.SingleAsync(x => x.Id == task.Id)).StateId);
        Assert.Single(await verify.WorkItems.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Patch_state_enforces_transition_permissions_and_updates_category()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var task = await Services(db, team).Work.CreateAsync(Request(team));
        var target = await db.WorkflowStates.FirstAsync(x => x.ProjectId == team.ProjectId && x.Category == WorkItemCategory.EnCurso);
        db.Transitions.Add(new Transition { ProjectId = team.ProjectId, OrganizationId = team.OrgId, FromStateId = task.StateId,
            ToStateId = target.Id, Name = "Approval required", RequiredPermissions = [Permission.Aprobar] });
        var editor = await AddMemberAsync(db, team, Permission.Leer, Permission.Editar, Permission.CambiarEstado);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => Services(db, editor).Work.PatchAsync(task.Id, new(StateId: target.Id, RowVersion: task.RowVersion)));
        var accepted = await Services(db, team).Work.PatchAsync(task.Id, new(StateId: target.Id, RowVersion: task.RowVersion));
        Assert.Equal(target.Id, accepted.StateId);
        Assert.Equal(WorkItemCategory.EnCurso, accepted.Category);
    }

    [Fact]
    public async Task Assignment_rejects_project_member_without_effective_read_permission()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var unreadable = await AddMemberAsync(db, team, Permission.Crear);
        var work = Services(db, team).Work;
        await Assert.ThrowsAnyAsync<AppException>(() => work.CreateAsync(Request(team) with { AssigneeId = unreadable.UserId }));
        Assert.False(await db.WorkItems.AnyAsync(x => x.ProjectId == team.ProjectId));
    }

    [Fact]
    public async Task Nondefault_creation_state_enforces_transition_permissions_and_conditions()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var target = await db.WorkflowStates.FirstAsync(x => x.ProjectId == team.ProjectId && x.Category == WorkItemCategory.EnCurso);
        db.Transitions.Add(new Transition { OrganizationId = team.OrgId, ProjectId = team.ProjectId,
            FromStateId = team.StateId, ToStateId = target.Id, Name = "Ready requirements",
            RequiredPermissions = [Permission.Aprobar], Conditions = "{\"requireDescription\":true}" });
        await db.SaveChangesAsync();
        var creator = await AddMemberAsync(db, team, Permission.Leer, Permission.Crear, Permission.CambiarEstado);
        var request = Request(team) with { StateId = target.Id, Description = "Complete" };
        await Assert.ThrowsAsync<ForbiddenException>(() => Services(db, creator).Work.CreateAsync(request));
        await using (var missing = database.NewDb())
            await Assert.ThrowsAnyAsync<AppException>(() => Services(missing, team).Work.CreateAsync(request with { Description = null }));
        await using var valid = database.NewDb();
        var created = await Services(valid, team).Work.CreateAsync(request);
        Assert.Equal(target.Id, created.StateId);
        Assert.Equal(WorkItemCategory.EnCurso, created.Category);
        Assert.Single(await valid.WorkItems.Where(x => x.ProjectId == team.ProjectId).ToListAsync());
    }

    [Fact]
    public async Task Older_full_edit_omitting_map_fields_preserves_them()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var work = Services(db, team).Work;
        var task = await work.CreateAsync(Request(team) with { Notes = "Preserve note", ProgressPercent = 40, AccentColor = "#ABCDEF" });
        var edited = await work.UpdateAsync(task.Id, Update(task) with {
            Title = "Edited elsewhere", Notes = null, ProgressPercent = null, AccentColor = null });
        Assert.Equal("Edited elsewhere", edited.Title);
        Assert.Equal("Preserve note", edited.Notes);
        Assert.Equal(40, edited.ProgressPercent);
        Assert.Equal("#ABCDEF", edited.AccentColor);
    }

    [Fact]
    public async Task Clearing_manual_progress_preserves_state_and_child_progress_is_independent()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var work = Services(db, team).Work;
        var parent = await work.CreateAsync(Request(team) with { ProgressPercent = 40, Notes = "Parent note" });
        var child = await work.CreateChildAsync(parent.Id, Request(team));
        Assert.Null(child.ProgressPercent);
        Assert.Null(child.Notes);
        parent = await work.GetAsync(parent.Id);
        var cleared = await work.PatchAsync(parent.Id, new(RowVersion: parent.RowVersion, ClearProgressPercent: true));
        Assert.Null(cleared.ProgressPercent);
        Assert.Equal(parent.StateId, cleared.StateId);
        Assert.Equal(parent.Category, cleared.Category);
        Assert.Equal("Parent note", cleared.Notes);
    }

    [Fact]
    public async Task Reader_can_save_personal_camera_but_cannot_edit_create_move_or_archive()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var task = await Services(db, team).Work.CreateAsync(Request(team));
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var (work, diagram) = Services(db, reader);
        var node = Assert.Single((await diagram.GetAsync(team.ProjectId)).Tasks);
        Assert.False(node.CanEdit);
        Assert.False(node.CanCreateChild);
        Assert.False(node.CanArchive);
        Assert.False(node.CanChangeState);
        await Assert.ThrowsAsync<ForbiddenException>(() => work.CreateChildAsync(task.Id, Request(team)));
        await Assert.ThrowsAsync<ForbiddenException>(() => work.PatchAsync(task.Id, new(Title: "No", RowVersion: task.RowVersion)));
        await Assert.ThrowsAsync<ForbiddenException>(() => diagram.SavePositionsAsync(team.ProjectId, [new(task.Id, 20, 30, node.PositionRowVersion)]));
        await Assert.ThrowsAsync<ForbiddenException>(() => work.ArchiveAsync(task.Id, new(task.RowVersion, true)));
        await diagram.SaveViewportAsync(team.ProjectId, new(111, 222, 1.2, [task.Id]));
        var personal = await diagram.GetAsync(team.ProjectId);
        Assert.Equal((111d, 222d, 1.2d), (personal.ViewportX, personal.ViewportY, personal.Zoom));
        Assert.Contains(task.Id, personal.CollapsedTaskIds);
        var adminView = await Services(db, team).Diagram.GetAsync(team.ProjectId);
        Assert.Empty(adminView.CollapsedTaskIds);
        Assert.NotEqual(111, adminView.ViewportX);
    }

    [Fact]
    public async Task Revoked_membership_blocks_existing_user_context()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var (_, map) = Services(db, reader);
        await map.GetAsync(team.ProjectId);
        var member = await db.OrganizationMembers.SingleAsync(x => x.UserId == reader.UserId && x.OrganizationId == team.OrgId);
        member.IsActive = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ForbiddenException>(() => map.GetAsync(team.ProjectId));
        await Assert.ThrowsAsync<ForbiddenException>(() => map.SaveViewportAsync(team.ProjectId, new(1, 2, 1)));
    }

    [Fact]
    public async Task Reader_cannot_mutate_checklists_or_block_condition()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var work = Services(db, team).Work;
        var task = await work.CreateAsync(Request(team));
        var checklist = await work.AddChecklistAsync(task.Id, "Checks");
        var item = await work.AddChecklistItemAsync(checklist.Id, "Keep pending", null);
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        var readOnly = Services(db, reader).Work;
        await Assert.ThrowsAsync<ForbiddenException>(() => readOnly.AddChecklistAsync(task.Id, "Forbidden"));
        await Assert.ThrowsAsync<ForbiddenException>(() => readOnly.AddChecklistItemAsync(checklist.Id, "Forbidden", null));
        await Assert.ThrowsAsync<ForbiddenException>(() => readOnly.ToggleChecklistItemAsync(item.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => readOnly.SetBlockedAsync(task.Id, true, "Forbidden"));
        await using var verify = database.NewDb();
        Assert.False((await verify.ChecklistItems.SingleAsync(x => x.Id == item.Id)).IsCompleted);
        Assert.False((await verify.WorkItems.SingleAsync(x => x.Id == task.Id)).IsBlocked);
        Assert.Single(await verify.Checklists.Where(x => x.WorkItemId == task.Id).ToListAsync());
    }

    [Fact]
    public async Task Revoked_project_access_excludes_task_notification_body_and_count()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var task = await Services(db, team).Work.CreateAsync(Request(team));
        var reader = await AddMemberAsync(db, team, Permission.Leer);
        db.Notifications.Add(new Notification { OrganizationId = team.OrgId, UserId = reader.UserId,
            Event = NotificationEvent.Mencion, Message = "Restricted task title", EntityType = "WorkItem", EntityId = task.Id,
            Route = $"/tasks/{task.Id}", DedupKey = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();
        var current = new TestUser(reader.UserId, reader.OrgId);
        var notifications = new NotificationService(db, current, new PermissionService(db, current));
        Assert.Equal(1, (await notifications.ListAsync(false, 1, 100)).Total);
        Assert.Equal(1, await notifications.UnreadCountAsync());
        (await db.ProjectMembers.SingleAsync(x => x.ProjectId == team.ProjectId && x.UserId == reader.UserId)).IsActive = false;
        await db.SaveChangesAsync();
        var revoked = await notifications.ListAsync(false, 1, 100);
        Assert.Equal(0, revoked.Total);
        Assert.Empty(revoked.Items);
        Assert.Equal(0, await notifications.UnreadCountAsync());
    }

    [Fact]
    public async Task Archive_and_restore_confirm_whole_branch_and_preserve_positions_and_links()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, team);
        var root = await work.CreateAsync(Request(team, "Root"));
        var child = await work.CreateChildAsync(root.Id, Request(team, "Child"));
        var grandchild = await work.CreateChildAsync(child.Id, Request(team, "Grandchild"));
        var unrelated = await work.CreateAsync(Request(team, "Unrelated"));
        root = await work.GetAsync(root.Id);
        var before = (await diagram.GetAsync(team.ProjectId)).Tasks.ToDictionary(x => x.Id, x => (x.X, x.Y));
        Assert.Equal(3, (await work.ArchivePreviewAsync(root.Id)).Count);
        await Assert.ThrowsAnyAsync<AppException>(() => work.ArchiveAsync(root.Id, new(root.RowVersion, false)));
        await work.ArchiveAsync(root.Id, new(root.RowVersion, true));
        Assert.Equal(unrelated.Id, Assert.Single((await diagram.GetAsync(team.ProjectId)).Tasks).Id);
        var archived = await diagram.GetAsync(team.ProjectId, true);
        Assert.Equal(3, archived.Tasks.Count(x => x.IsArchived));
        var rootVersion = archived.Tasks.Single(x => x.Id == root.Id).RowVersion;
        Assert.Equal(3, (await work.ArchivePreviewAsync(root.Id, true)).Count);
        await work.ArchiveAsync(root.Id, new(rootVersion, true), true);
        var restored = await diagram.GetAsync(team.ProjectId);
        Assert.Equal(4, restored.Tasks.Count);
        Assert.All(restored.Tasks, x => Assert.Equal(before[x.Id], (x.X, x.Y)));
        Assert.Equal(root.Id, restored.Tasks.Single(x => x.Id == child.Id).ParentTaskId);
        Assert.Equal(child.Id, restored.Tasks.Single(x => x.Id == grandchild.Id).ParentTaskId);
        Assert.DoesNotContain(restored.Tasks, x => x.IsArchived);
    }

    [Fact]
    public async Task Position_and_viewport_validation_rejects_nonfinite_coordinates_and_duplicate_tasks()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, team);
        var task = await work.CreateAsync(Request(team));
        var node = Assert.Single((await diagram.GetAsync(team.ProjectId)).Tasks);
        await Assert.ThrowsAnyAsync<AppException>(() => diagram.SavePositionsAsync(team.ProjectId, [new(task.Id, double.NaN, 0, node.PositionRowVersion)]));
        await Assert.ThrowsAnyAsync<AppException>(() => diagram.SavePositionsAsync(team.ProjectId,
            [new(task.Id, 0, 0, node.PositionRowVersion), new(task.Id, 1, 1, node.PositionRowVersion)]));
        await Assert.ThrowsAnyAsync<AppException>(() => diagram.SaveViewportAsync(team.ProjectId, new(0, double.PositiveInfinity, 1)));
        await Assert.ThrowsAnyAsync<AppException>(() => diagram.SaveViewportAsync(team.ProjectId, new(0, 0, double.NaN)));
    }

    [Fact]
    public async Task Rename_updates_descendant_paths_without_changing_child_identity_or_title()
    {
        await using var db = database.NewDb();
        var team = await SeedTeamAsync(db);
        var (work, diagram) = Services(db, team);
        var parent = await work.CreateAsync(Request(team, "Parent"));
        var child = await work.CreateChildAsync(parent.Id, Request(team, "Own child title"));
        parent = await work.GetAsync(parent.Id);
        await work.PatchAsync(parent.Id, new(Title: "Renamed", RowVersion: parent.RowVersion));
        var node = (await diagram.GetAsync(team.ProjectId)).Tasks.Single(x => x.Id == child.Id);
        Assert.Equal("Own child title", node.Title);
        Assert.Equal(parent.Id, node.ParentTaskId);
        Assert.Contains("Renamed", node.FullPath);
        Assert.DoesNotContain("Parent", node.FullPath);
    }

    private static (WorkItemService Work, TaskDiagramService Diagram) Services(AppDbContext db, Team team)
    {
        var current = new TestUser(team.UserId, team.OrgId);
        var permissions = new PermissionService(db, current);
        return (new(db, current, permissions), new(db, current, permissions));
    }

    private static CreateWorkItemRequest Request(Team team, string title = "Test task") =>
        new(title, WorkItemType.TareaTecnica, team.ProjectId, null, Priority.Media, null, null, null);

    private static UpdateWorkItemRequest Update(WorkItemDetailDto x) => new(
        x.Title, x.Type, x.AssigneeId, x.Priority, x.Description, x.Objective, x.Context,
        x.StartDate, x.DueDate, x.EstimateHours, x.CycleId, x.EpicId, x.ReleaseId, x.StateId,
        x.AcceptanceCriteria, x.Labels, x.ReproSteps, x.ExpectedResult, x.ActualResult,
        x.Environment, x.AffectedVersion, x.Severity, x.ResearchQuestion, x.ResearchConclusion,
        x.ParentTaskId, x.Notes, x.ProgressPercent, x.AccentColor, RowVersion: x.RowVersion);

    private static async Task<Team> SeedTeamAsync(AppDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var org = new Organization { Name = "Map tests", Slug = $"map-{suffix}" };
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"admin-{suffix}@test.invalid", FullName = "Test admin" };
        var role = new Role { OrganizationId = org.Id, Name = "Admin", Permissions = Enum.GetValues<Permission>().ToList() };
        db.Organizations.Add(org);
        db.Users.Add(user);
        db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync();
        db.OrganizationMembers.Add(new() { OrganizationId = org.Id, UserId = user.Id, RoleId = role.Id, Name = user.FullName, IsActive = true });
        await db.SaveChangesAsync();
        return await SeedProjectAsync(db, new(org.Id, user.Id, Guid.Empty, Guid.Empty));
    }

    private static async Task<Team> SeedProjectAsync(AppDbContext db, Team team)
    {
        var project = new Project { OrganizationId = team.OrgId, Name = "Map integration", Code = $"T{Guid.NewGuid():N}"[..12], IsPrivate = true };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        await db.EnsureDefaultStatesAsync(project.Id, team.OrgId);
        await db.SaveChangesAsync();
        return team with { ProjectId = project.Id, StateId = await db.WorkflowStates.Where(x => x.ProjectId == project.Id && x.IsDefault).Select(x => x.Id).SingleAsync() };
    }

    private static async Task<Team> AddMemberAsync(AppDbContext db, Team team, params Permission[] permissions)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new AppUser { Id = Guid.NewGuid(), UserName = $"member-{suffix}@test.invalid", FullName = "Test member" };
        var role = new Role { OrganizationId = team.OrgId, Name = $"Role-{suffix}", Permissions = permissions.ToList() };
        db.Users.Add(user);
        db.OrganizationRoles.Add(role);
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
        public string Name => "SQL map test";
        public bool IsAuthenticated => true;
    }
}
