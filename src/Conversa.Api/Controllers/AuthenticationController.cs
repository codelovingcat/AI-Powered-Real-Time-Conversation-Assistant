using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conversa.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth")]
public sealed class AuthenticationController : ControllerBase
{
    [HttpGet("session")]
    public IActionResult Session()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(rawUserId, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized();
        }

        return Ok(new AuthSessionResponse(true, userId));
    }
}

public sealed record AuthSessionResponse(bool Authenticated, Guid UserId);
