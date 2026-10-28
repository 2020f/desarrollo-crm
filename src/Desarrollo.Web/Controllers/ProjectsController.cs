using Desarrollo.Application.Abstractions;
using Desarrollo.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _service;
    public ProjectsController(IProjectService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] ProjectStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.ListAsync(new ProjectListQuery(search, status, page, pageSize)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) => Ok(await _service.GetAsync(id));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest req) => Ok(await _service.CreateAsync(req));

    [HttpPut("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProjectRequest req)
    {
        await _service.UpdateAsync(id, req);
        return Ok(new { ok = true });
    }

    [HttpGet("{id:guid}/states")]
    public async Task<IActionResult> States(Guid id) => Ok(await _service.GetStatesAsync(id));

    [HttpGet("{id:guid}/epics")]
    public async Task<IActionResult> Epics(Guid id) => Ok(await _service.GetEpicsAsync(id));

    [HttpGet("{id:guid}/cycles")]
    public async Task<IActionResult> Cycles(Guid id) => Ok(await _service.GetCyclesAsync(id));

    [HttpGet("{id:guid}/releases")]
    public async Task<IActionResult> Releases(Guid id) => Ok(await _service.GetReleasesAsync(id));

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> Members(Guid id) => Ok(await _service.GetProjectMembersAsync(id));

    [HttpPost("{id:guid}/members")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req) => Ok(await _service.AddMemberAsync(id, req.UserId, req.RoleId));

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId) { await _service.RemoveMemberAsync(id, userId); return Ok(new { ok = true }); }

    [HttpPut("{id:guid}/members/{userId:guid}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid userId, [FromBody] AddMemberRequest req) { await _service.UpdateMemberRoleAsync(id, userId, req.RoleId); return Ok(new { ok = true }); }

    public record AddMemberRequest(Guid UserId, Guid? RoleId);
}
