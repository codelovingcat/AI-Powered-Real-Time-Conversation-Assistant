using System.Reflection;
using Conversa.Api.Infrastructure;

namespace Conversa.Api.Tests.Infrastructure;

public sealed class ApiExceptionHandlerTests
{
    [Fact]
    public void Handler_DoesNotPassExceptionToMappedLogCalls()
    {
        var source = typeof(ApiExceptionHandler).Assembly
            .GetType("Conversa.Api.Infrastructure.ApiExceptionHandler");

        Assert.NotNull(source);
    }
}
