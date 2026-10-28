using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/task-diagram")]
[Authorize]
public class TaskDiagramController : ControllerBase
{
    private readonly ITaskDiagramService _service;
    public TaskDiagramController(ITaskDiagramService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, [FromQuery] bool includeArchived = false) => Ok(await _service.GetAsync(projectId, includeArchived));

    [HttpPatch("positions")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePositions(Guid projectId, [FromBody] SavePositionsRequest req)
    {
        return Ok(new { positions = await _service.SavePositionsAsync(projectId, req.Positions) });
    }

    [HttpPut("me/viewport")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveViewport(Guid projectId, [FromBody] SaveViewportRequest req)
    {
        await _service.SaveViewportAsync(projectId, req);
        return Ok(new { ok = true });
    }
}
