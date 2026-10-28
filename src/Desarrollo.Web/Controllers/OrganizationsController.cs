using Desarrollo.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/organizations")]
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _service;
    public OrganizationsController(IOrganizationService service) => _service = service;

    [HttpGet("current")]
    public async Task<IActionResult> Current() => Ok(await _service.GetCurrentAsync());

    [HttpGet("members")]
    public async Task<IActionResult> Members() => Ok(await _service.GetMembersAsync());

    [HttpGet("roles")]
    public async Task<IActionResult> Roles() => Ok(await _service.GetRolesAsync());

    [HttpGet("products")]
    public async Task<IActionResult> Products() => Ok(await _service.GetProductsAsync());
}
