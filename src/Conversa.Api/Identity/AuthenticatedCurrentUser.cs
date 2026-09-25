using System.Security.Claims;
using Conversa.Application.Abstractions.Identity;

namespace Conversa.Api.Identity;

public sealed class AuthenticatedCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                throw new InvalidOperationException("An authenticated user is required.");
            }

            var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub");

            return Guid.TryParse(raw, out var userId) && userId != Guid.Empty
                ? userId
                : throw new InvalidOperationException("The authenticated user identifier is invalid.");
        }
    }
}
