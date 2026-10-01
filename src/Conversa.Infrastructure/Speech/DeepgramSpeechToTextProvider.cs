using System.Net.Http.Headers;
using System.Text.Json;
using Conversa.Application.Speech;
using Microsoft.Extensions.Options;

namespace Conversa.Infrastructure.Speech;

public sealed class DeepgramSpeechToTextProvider(
    HttpClient httpClient,
    IOptions<DeepgramOptions> options)
    : ISpeechToTextProvider
{
    private const string ListenPath = "/v1/listen";

    public string ProviderName => "deepgram";

    public async Task<SpeechRecognitionResult> TranscribeAsync(
        SpeechAudio audio,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        settings.Validate();

        if (audio.Content.IsEmpty)
        {
            return new SpeechRecognitionResult(
                string.Empty,
                true,
                null,
                SpeechRecognitionStatus.Failed,
                new SpeechError("empty_audio", "The supplied audio payload is empty."));
        }

        var parameters = BuildParameters(
            settings,
            audio.Language,
            audio.ContentType,
            sampleRateHertz: null);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(settings.BaseUrl, ListenPath, parameters));

        request.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.ApiKey);
        request.Content = new ByteArrayContent(audio.Content.ToArray());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            DeepgramAudioFormat.GetContentType(audio));

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new SpeechRecognitionResult(
                    string.Empty,
                    true,
                    null,
                    SpeechRecognitionStatus.Failed,
                    new SpeechError(
                        $"http_{(int)response.StatusCode}",
                        "The speech-to-text provider could not process the audio."));
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

            return DeepgramTranscriptParser.ParsePreRecordedResult(document.RootElement.GetRawText());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SpeechRecognitionResult(
                string.Empty,
                true,
                null,
                SpeechRecognitionStatus.Cancelled,
                new SpeechError("cancelled", "Speech transcription was cancelled."));
        }
        catch (HttpRequestException)
        {
            return new SpeechRecognitionResult(
                string.Empty,
                true,
                null,
                SpeechRecognitionStatus.Failed,
                new SpeechError("provider_unavailable", "The speech-to-text provider is unavailable."));
        }
        catch (JsonException)
        {
            return new SpeechRecognitionResult(
                string.Empty,
                true,
                null,
                SpeechRecognitionStatus.Failed,
                new SpeechError("invalid_provider_response", "The speech-to-text provider returned an invalid response."));
        }
    }

    internal static List<KeyValuePair<string, string>> BuildParameters(
        DeepgramOptions settings,
        string language,
        string? contentType,
        int? sampleRateHertz)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("model", settings.Model),
            new("language", language),
            new("interim_results", "true"),
            new("punctuate", "true"),
            new("smart_format", "true")
        };

        if (settings.EndpointingMilliseconds > 0)
        {
            parameters.Add(new(
                "endpointing",
                settings.EndpointingMilliseconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        DeepgramAudioFormat.ApplyQueryParameters(parameters, contentType, sampleRateHertz);
        return parameters;
    }

    internal static Uri BuildUri(
        string baseUrl,
        string path,
        IEnumerable<KeyValuePair<string, string>> parameters)
    {
        var builder = new UriBuilder(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        var query = string.Join(
            "&",
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));

        builder.Query = query;
        return builder.Uri;
    }
}
