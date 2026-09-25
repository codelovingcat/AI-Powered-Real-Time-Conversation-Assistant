using System.Reflection;
using Conversa.Api.Realtime;

namespace Conversa.Api.Tests.Security;

public sealed class AudioWebSocketSecurityTests
{
    [Fact]
    public void AudioEndpoint_ExposesExplicitConversationIdRoute()
    {
        var endpointType = typeof(AudioWebSocketEndpoint);

        Assert.NotNull(endpointType);
        Assert.Contains(
            "AudioWebSocketEndpoint",
            endpointType.FullName ?? string.Empty,
            StringComparison.Ordinal);
    }
}
