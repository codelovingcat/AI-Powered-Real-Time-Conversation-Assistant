using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Common;

namespace Conversa.Api.Identity;

public sealed class HeaderCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string HeaderName = "X-User-Id";
    public const string QueryName = "userId";

    public Guid UserId => Resolve(httpContextAccessor.HttpContext);

    public static bool TryResolve(HttpContext? httpContext, out Guid userId)
    {
        userId = Guid.Empty;
        if (httpContext is null)
        {
            return false;
        }

        var raw = httpContext.Request.Headers[HeaderName].FirstOrDefault()
            ?? httpContext.Request.Query[QueryName].FirstOrDefault();

        return Guid.TryParse(raw, out userId) && userId != Guid.Empty;
    }

    private static Guid Resolve(HttpContext? httpContext)
    {
        if (!TryResolve(httpContext, out var userId))
        {
            throw ValidationException.For(
                HeaderName,
                $"Provide a GUID in the {HeaderName} header. This is a temporary stand-in until authentication is implemented.");
        }

        return userId;
    }
}
