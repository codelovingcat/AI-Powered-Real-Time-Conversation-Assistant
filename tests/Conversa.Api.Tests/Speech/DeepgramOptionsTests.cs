using Conversa.Infrastructure.Speech;

namespace Conversa.Api.Tests.Speech;

public sealed class DeepgramOptionsTests
{
    [Fact]
    public void Validate_accepts_valid_configuration()
    {
        var options = new DeepgramOptions
        {
            ApiKey = "test-key",
            Model = "nova-3",
            BaseUrl = "https://api.deepgram.com",
            EndpointingMilliseconds = 300
        };

        options.Validate();
    }

    [Fact]
    public void Validate_rejects_missing_api_key()
    {
        var options = new DeepgramOptions();

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Deepgram:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_invalid_base_url()
    {
        var options = new DeepgramOptions
        {
            ApiKey = "test-key",
            BaseUrl = "not-a-uri"
        };

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("Deepgram:BaseUrl", exception.Message, StringComparison.Ordinal);
    }
}
