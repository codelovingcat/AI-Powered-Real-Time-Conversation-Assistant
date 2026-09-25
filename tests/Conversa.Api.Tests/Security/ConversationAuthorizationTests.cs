using Conversa.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Conversa.Api.Tests.Security;

public sealed class ConversationAuthorizationTests
{
    [Fact]
    public void ConversationController_RequiresAuthentication()
    {
        var attribute = Attribute.GetCustomAttribute(
            typeof(ConversationsController),
            typeof(AuthorizeAttribute));

        Assert.NotNull(attribute);
    }
}
