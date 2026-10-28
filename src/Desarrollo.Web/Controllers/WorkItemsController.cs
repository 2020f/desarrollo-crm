using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/workitems")]
[Authorize]
public class WorkItemsController : ControllerBase
{
    private readonly IWorkItemService _service;
    public WorkItemsController(IWorkItemService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] Guid? projectId, [FromQuery] string? search, [FromQuery] Guid? assigneeId,
        [FromQuery] Priority? priority, [FromQuery] WorkItemType? type, [FromQuery] Guid? stateId,
        [FromQuery] bool? blocked, [FromQuery] Guid? epicId, [FromQuery] Guid? cycleId,
        [FromQuery] bool? overdue, [FromQuery] string? label, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 100, [FromQuery] bool includeArchived = false)
    {
        var q = new WorkItemQuery(projectId, search, assigneeId, priority, type, stateId, blocked, epicId, cycleId, overdue, label, sort, page, pageSize, includeArchived);
        return Ok(await _service.QueryAsync(q));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateWorkItemRequest req) => Ok(await _service.CreateAsync(req));

    [HttpPost("{parentId:guid}/children")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateChild(Guid parentId, [FromBody] CreateWorkItemRequest req) => Ok(await _service.CreateChildAsync(parentId, req));

    [HttpPatch("{id:guid}/parent")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeParent(Guid id, [FromBody] ChangeParentRequest req) => Ok(await _service.ChangeParentAsync(id, req.ParentTaskId, req.RowVersion));

    [HttpGet("{id:guid}/archive-preview")]
    public async Task<IActionResult> ArchivePreview(Guid id, [FromQuery] bool restore = false) => Ok(await _service.ArchivePreviewAsync(id, restore));

    [HttpPost("{id:guid}/archive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveWorkItemRequest req) => Ok(await _service.ArchiveAsync(id, req));

    [HttpPost("{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, [FromBody] ArchiveWorkItemRequest req) => Ok(await _service.ArchiveAsync(id, req, restore: true));

    [HttpPut("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkItemRequest req) => Ok(await _service.UpdateAsync(id, req));

    [HttpPatch("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchWorkItemRequest req) => Ok(await _service.PatchAsync(id, req));

    [HttpPost("{id:guid}/transition")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionRequest req) => Ok(await _service.TransitionAsync(id, req.ToStateId, req.Reason, req.RowVersion));

    [HttpGet("{id:guid}/transitions")]
    public async Task<IActionResult> Transitions(Guid id) => Ok(await _service.GetAvailableTransitionsAsync(id));

    [HttpGet("{id:guid}/comments")]
    public async Task<IActionResult> Comments(Guid id) => Ok(await _service.GetCommentsAsync(id));

    [HttpPost("{id:guid}/comments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] CommentRequest req) => Ok(await _service.AddCommentAsync(id, req.Body));

    [HttpGet("{id:guid}/checklists")]
    public async Task<IActionResult> Checklists(Guid id) => Ok(await _service.GetChecklistsAsync(id));

    [HttpPost("{id:guid}/checklists")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddChecklist(Guid id, [FromBody] ChecklistRequest req) => Ok(await _service.AddChecklistAsync(id, req.Title));

    [HttpPost("checklist-items")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddChecklistItem([FromBody] ChecklistItemRequest req) => Ok(await _service.AddChecklistItemAsync(req.ChecklistId, req.Text, req.AssigneeId));

    [HttpPost("checklist-items/{itemId:guid}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleItem(Guid itemId) { await _service.ToggleChecklistItemAsync(itemId); return Ok(new { ok = true }); }

    [HttpPost("{id:guid}/block")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Block(Guid id, [FromBody] BlockRequest req) { await _service.SetBlockedAsync(id, req.Blocked, req.Reason); return Ok(new { ok = true }); }

    [HttpPost("{id:guid}/relations")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Relation(Guid id, [FromBody] RelationRequest req) { await _service.AddRelationAsync(id, req.TargetId, req.Type); return Ok(new { ok = true }); }

    public record TransitionRequest(Guid ToStateId, string? Reason, string? RowVersion = null);
    public record CommentRequest(string Body);
    public record ChecklistRequest(string Title);
    public record ChecklistItemRequest(Guid ChecklistId, string Text, Guid? AssigneeId);
    public record BlockRequest(bool Blocked, string? Reason);
    public record RelationRequest(Guid TargetId, WorkRelationType Type);
}
