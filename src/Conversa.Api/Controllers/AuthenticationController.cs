using System.Security.Claims;
using Conversa.Api.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conversa.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(
    IConfiguration configuration,
    JwtAccessTokenFactory accessTokenFactory) : ControllerBase
{
    private const string PersistentSessionScheme = "Conversa.Session";
    private const string AccessTokenLifetimeMinutesKey = "Authentication:AccessTokenLifetimeMinutes";
    private const int DefaultSessionLifetimeDays = 7;

    [HttpGet("session")]
    [Authorize]
    public IActionResult Session()
    {
        if (!TryGetUserId(User, out var userId))
        {
            return Unauthorized();
        }

        return Ok(new AuthSessionResponse(true, userId));
    }

    [HttpPost("session")]
    [AllowAnonymous]
    public async Task<IActionResult> EstablishSession(CancellationToken cancellationToken)
    {
        var originResult = ValidateOrigin();
        if (originResult is not null)
        {
            return originResult;
        }

        var result = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!result.Succeeded || !TryGetUserId(result.Principal, out var userId))
        {
            return Unauthorized();
        }

        var sessionLifetimeDays =
            configuration.GetValue("Authentication:SessionLifetimeDays", DefaultSessionLifetimeDays);

        await HttpContext.SignInAsync(
            PersistentSessionScheme,
            CreateSessionPrincipal(userId),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(sessionLifetimeDays)
            });

        return Ok(CreateAccessTokenResponse(userId));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var originResult = ValidateOrigin();
        if (originResult is not null)
        {
            return originResult;
        }

        var result = await HttpContext.AuthenticateAsync(PersistentSessionScheme);
        if (!result.Succeeded || !TryGetUserId(result.Principal, out var userId))
        {
            await HttpContext.SignOutAsync(PersistentSessionScheme);
            return Unauthorized();
        }

        return Ok(CreateAccessTokenResponse(userId));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var originResult = ValidateOrigin();
        if (originResult is not null)
        {
            return originResult;
        }

        await HttpContext.SignOutAsync(PersistentSessionScheme);
        return NoContent();
    }

    private TokenResponse CreateAccessTokenResponse(Guid userId)
    {
        var token = accessTokenFactory.Create(userId);
        return new TokenResponse(true, userId, token.Token, token.ExpiresAt);
    }

    private IActionResult? ValidateOrigin()
    {
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin))
        {
            return null;
        }

        var sameOrigin = $"{Request.Scheme}://{Request.Host}";
        if (string.Equals(origin, sameOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var allowedOrigins = (configuration["Cors:AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return allowedOrigins.Any(candidate =>
                string.Equals(candidate, origin, StringComparison.OrdinalIgnoreCase))
            ? null
            : StatusCode(StatusCodes.Status403Forbidden);
    }

    private static ClaimsPrincipal CreateSessionPrincipal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: PersistentSessionScheme);

        return new ClaimsPrincipal(identity);
    }

    private static bool TryGetUserId(ClaimsPrincipal? principal, out Guid userId)
    {
        var rawUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");

        return Guid.TryParse(rawUserId, out userId) && userId != Guid.Empty;
    }
}

public sealed record AuthSessionResponse(bool Authenticated, Guid UserId);

public sealed record TokenResponse(
    bool Authenticated,
    Guid UserId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);
