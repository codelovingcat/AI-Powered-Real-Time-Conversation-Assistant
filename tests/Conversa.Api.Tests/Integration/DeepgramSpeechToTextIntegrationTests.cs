using Conversa.Application.Speech;
using Conversa.Infrastructure.Speech;
using Microsoft.Extensions.Options;

namespace Conversa.Api.Tests.Integration;

public sealed class DeepgramSpeechToTextIntegrationTests
{
    private const string SampleAudioUrl =
        "https://static.deepgram.com/examples/Bueller-Life-moves-pretty-fast.wav";

    [DeepgramIntegrationFact]
    public async Task Deepgram_transcribes_a_real_english_audio_sample()
    {
        var apiKey = Environment.GetEnvironmentVariable("Deepgram__ApiKey");
        Assert.False(
            string.IsNullOrWhiteSpace(apiKey),
            "Deepgram__ApiKey must be configured.");

        var model = Environment.GetEnvironmentVariable("Deepgram__Model") ?? "nova-3";
        var baseUrl = Environment.GetEnvironmentVariable("Deepgram__BaseUrl") ?? "https://api.deepgram.com";

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        using var audioResponse = await httpClient.GetAsync(
            SampleAudioUrl,
            HttpCompletionOption.ResponseHeadersRead);

        audioResponse.EnsureSuccessStatusCode();

        var audioContent = await audioResponse.Content.ReadAsByteArrayAsync();

        var provider = new DeepgramSpeechToTextProvider(
            httpClient,
            Options.Create(new DeepgramOptions
            {
                ApiKey = apiKey!,
                Model = model,
                BaseUrl = baseUrl
            }));

        var result = await provider.TranscribeAsync(
            new SpeechAudio(audioContent, "audio/wav", "en-US"),
            CancellationToken.None);

        Assert.Equal(SpeechRecognitionStatus.Completed, result.Status);
        Assert.True(
            !string.IsNullOrWhiteSpace(result.Text),
            "Deepgram returned an empty transcript.");
    }
}
