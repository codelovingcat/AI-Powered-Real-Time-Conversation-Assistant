using Conversa.Infrastructure.Speech;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Tests.Speech;

public sealed class DeepgramSpeechToTextProviderTests
{
    [Fact]
    public void BuildStreamingParameters_configures_live_transcription_defaults()
    {
        var settings = new DeepgramOptions
        {
            ApiKey = "test-key",
            Model = "nova-3",
            BaseUrl = "https://api.deepgram.com",
            EndpointingMilliseconds = 300
        };

        var parameters = DeepgramSpeechToTextProvider.BuildStreamingParameters(
            settings,
            "en-US",
            "audio/webm;codecs=opus",
            null);

        Assert.Contains(parameters, parameter => parameter.Key == "model" && parameter.Value == "nova-3");
        Assert.Contains(parameters, parameter => parameter.Key == "language" && parameter.Value == "en-US");
        Assert.Contains(parameters, parameter => parameter.Key == "interim_results" && parameter.Value == "true");
        Assert.Contains(parameters, parameter => parameter.Key == "endpointing" && parameter.Value == "300");
        Assert.DoesNotContain(parameters, parameter => parameter.Key == "encoding");
    }

    [Fact]
    public void BuildParameters_requires_sample_rate_for_raw_pcm()
    {
        var settings = new DeepgramOptions { ApiKey = "test-key" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => DeepgramSpeechToTextProvider.BuildParameters(
                settings,
                "en-US",
                "audio/pcm",
                null));

        Assert.Contains("sample rate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUri_contains_only_expected_public_parameters()
    {
        var settings = new DeepgramOptions
        {
            ApiKey = "secret-value"
        };

        var uri = DeepgramSpeechToTextProvider.BuildUri(
            settings.BaseUrl,
            "/v1/listen",
            DeepgramSpeechToTextProvider.BuildPreRecordedParameters(
                settings,
                "en-US",
                "audio/wav"));

        Assert.Contains("model=nova-3", uri.Query, StringComparison.Ordinal);
        Assert.Contains("language=en-US", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", uri.AbsoluteUri, StringComparison.Ordinal);
    }
}
