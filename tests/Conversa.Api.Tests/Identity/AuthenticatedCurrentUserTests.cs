using System.Security.Claims;
using Conversa.Api.Identity;
using Microsoft.AspNetCore.Http;

namespace Conversa.Api.Tests.Identity;

public sealed class AuthenticatedCurrentUserTests
{
    [Fact]
    public void UserId_UsesValidatedNameIdentifierClaim()
    {
        var expected = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, expected.ToString())],
                authenticationType: "Bearer"))
        };

        var currentUser = new AuthenticatedCurrentUser(
            new HttpContextAccessor { HttpContext = context });

        Assert.Equal(expected, currentUser.UserId);
    }

    [Fact]
    public void UserId_RejectsUnauthenticatedPrincipal()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };

        var currentUser = new AuthenticatedCurrentUser(
            new HttpContextAccessor { HttpContext = context });

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }

    [Fact]
    public void UserId_RejectsMissingOrInvalidIdentifier()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
                authenticationType: "Bearer"))
        };

        var currentUser = new AuthenticatedCurrentUser(
            new HttpContextAccessor { HttpContext = context });

        Assert.Throws<InvalidOperationException>(() => currentUser.UserId);
    }
}
