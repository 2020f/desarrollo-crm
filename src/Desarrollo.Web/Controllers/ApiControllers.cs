using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;
    public NotificationsController(INotificationService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
        => Ok(await _service.ListAsync(unreadOnly, page, pageSize));

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() => Ok(new { count = await _service.UnreadCountAsync() });

    [HttpPost("{id:guid}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(Guid id) { await _service.MarkReadAsync(id); return Ok(new { ok = true }); }

    [HttpPost("read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead() { await _service.MarkAllReadAsync(); return Ok(new { ok = true }); }
}

[ApiController]
[Route("api/channels")]
[Authorize]
public class ChannelsController : ControllerBase
{
    private readonly IChannelService _service;
    public ChannelsController(IChannelService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? projectId) => Ok(await _service.ListAsync(projectId));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateChannelRequest req) => Ok(await _service.CreateAsync(req.ProjectId, req.Name, req.Topic));

    [HttpPost("direct")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Direct([FromBody] DirectRequest req) => Ok(await _service.GetOrCreateDirectAsync(req.UserId));

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> Messages(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.GetMessagesAsync(id, page, pageSize));

    [HttpPost("{id:guid}/messages")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(Guid id, [FromBody] SendMessageRequest req) => Ok(await _service.SendAsync(id, req.Body, req.ThreadRootId));

    [HttpPut("messages/{messageId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid messageId, [FromBody] SendMessageRequest req) => Ok(await _service.EditAsync(messageId, req.Body));

    [HttpPost("{id:guid}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(Guid id) { await _service.MarkReadAsync(id); return Ok(new { ok = true }); }

    public record CreateChannelRequest(Guid? ProjectId, string Name, string? Topic);
    public record DirectRequest(Guid UserId);
    public record SendMessageRequest(string Body, Guid? ThreadRootId);
}

[ApiController]
[Route("api/documents")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _service;
    public DocumentsController(IDocumentService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid projectId) => Ok(await _service.ListAsync(projectId));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateDocumentRequest req) => Ok(await _service.CreateAsync(
        req.ProjectId, req.Title, req.ParentId, req.Content, req.Visibility, req.ReaderIds, req.ContentFormat));

    [HttpPut("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentRequest req) => Ok(await _service.UpdateAsync(
        id, req.Title, req.Content, req.Note, req.Visibility, req.ReaderIds, req.ContentFormat, req.RowVersion));

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> Versions(Guid id) => Ok(await _service.GetVersionsAsync(id));

    public record CreateDocumentRequest(Guid ProjectId, string Title, Guid? ParentId, string Content,
        Desarrollo.Domain.Docs.DocumentVisibility Visibility = Desarrollo.Domain.Docs.DocumentVisibility.Privada,
        List<Guid>? ReaderIds = null, string ContentFormat = "tiptap");
    public record UpdateDocumentRequest(string Title, string Content, string? Note = null,
        Desarrollo.Domain.Docs.DocumentVisibility? Visibility = null, List<Guid>? ReaderIds = null,
        string? ContentFormat = null, string? RowVersion = null);
}

[ApiController]
[Route("api/calendar")]
[Authorize]
public class CalendarController : ControllerBase
{
    private readonly ICalendarService _service;
    public CalendarController(ICalendarService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Range([FromQuery] DateTime start, [FromQuery] DateTime end, [FromQuery] string? timeZoneId = null) =>
        Ok(await _service.GetRangeAsync(start, end, timeZoneId));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateCalendarEventRequest req) => Ok(await _service.CreateAsync(req));

    [HttpPut("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateCalendarEventRequest req) => Ok(await _service.UpdateAsync(id, req));

    [HttpDelete("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id) { await _service.DeleteAsync(id); return Ok(new { ok = true }); }
}

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IReportService _service;
    public ReportsController(IReportService service) => _service = service;

    [HttpGet("project/{id:guid}")]
    public async Task<IActionResult> ProjectSnapshot(Guid id) => Ok(await _service.GetProjectSnapshotAsync(id));
}
