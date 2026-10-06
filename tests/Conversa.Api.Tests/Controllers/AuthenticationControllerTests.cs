using System.Security.Claims;
using Conversa.Api.Controllers;
using Conversa.Api.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Conversa.Api.Tests.Controllers;

public sealed class AuthenticationControllerTests
{
    [Fact]
    public void Session_ReturnsAuthenticatedUser()
    {
        var userId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var controller = CreateController(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                authenticationType: "Bearer")));

        var result = Assert.IsType<OkObjectResult>(controller.Session());
        var payload = Assert.IsType<AuthSessionResponse>(result.Value);

        Assert.True(payload.Authenticated);
        Assert.Equal(userId, payload.UserId);
    }

    [Fact]
    public void Session_RejectsAuthenticatedPrincipalWithoutValidUserId()
    {
        var controller = CreateController(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "not-a-guid")],
                authenticationType: "Bearer")));

        Assert.IsType<UnauthorizedResult>(controller.Session());
    }

    private static AuthenticationController CreateController(ClaimsPrincipal user)
    {
        var configuration = new ConfigurationManager();
        configuration["Authentication:SessionLifetimeDays"] = "7";
        configuration["Cors:AllowedOrigins"] = "http://localhost:5173";

        var factory = new JwtAccessTokenFactory(
            "conversa-test",
            "conversa-api",
            new SymmetricSecurityKey(new byte[32]),
            TimeSpan.FromMinutes(15),
            TimeProvider.System);

        return new AuthenticationController(configuration, factory)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = user
                }
            }
        };
    }
}
