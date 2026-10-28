using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
public class TicketsController : ControllerBase
{
    private readonly ITicketService _service;
    public TicketsController(ITicketService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? projectId, [FromQuery] TicketStatus? status, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.ListAsync(new TicketQuery(projectId, status, search, page, pageSize)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest req) => Ok(await _service.CreateAsync(req));

    [HttpPost("{id:guid}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetStatusRequest req) => Ok(await _service.SetStatusAsync(id, req.Status, req.Reason));

    [HttpPost("{id:guid}/assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignRequest req) => Ok(await _service.AssignAsync(id, req.AssigneeId));

    [HttpGet("{id:guid}/comments")]
    public async Task<IActionResult> Comments(Guid id) => Ok(await _service.GetCommentsAsync(id));

    [HttpPost("{id:guid}/comments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] TicketCommentRequest req) => Ok(await _service.AddCommentAsync(id, req.Body, req.IsInternal));

    [HttpPost("{id:guid}/link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(Guid id, [FromBody] LinkRequest req) { await _service.LinkWorkItemAsync(id, req.WorkItemId); return Ok(new { ok = true }); }

    public record SetStatusRequest(TicketStatus Status, string? Reason);
    public record AssignRequest(Guid? AssigneeId);
    public record TicketCommentRequest(string Body, bool IsInternal);
    public record LinkRequest(Guid WorkItemId);
}
