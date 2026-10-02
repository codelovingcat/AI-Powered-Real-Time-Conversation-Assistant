using System.Security.Claims;
using Conversa.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Conversa.Api.Tests.Controllers;

public sealed class AuthenticationControllerTests
{
    [Fact]
    public void Session_ReturnsAuthenticatedUser()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var controller = new AuthenticationController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                        authenticationType: "Bearer"))
                }
            }
        };

        var result = Assert.IsType<OkObjectResult>(controller.Session());
        var payload = Assert.IsType<AuthSessionResponse>(result.Value);

        Assert.True(payload.Authenticated);
        Assert.Equal(userId, payload.UserId);
    }

    [Fact]
    public void Session_RejectsAuthenticatedPrincipalWithoutValidUserId()
    {
        var controller = new AuthenticationController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
                        authenticationType: "Bearer"))
                }
            }
        };

        Assert.IsType<UnauthorizedResult>(controller.Session());
    }
}
