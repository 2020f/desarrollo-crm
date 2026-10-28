using Desarrollo.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/activity")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ActivityController(ILiveActivityService activity) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await activity.GetAsync());
}
