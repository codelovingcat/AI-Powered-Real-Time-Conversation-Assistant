using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conversa.Infrastructure.Ai.Gemini;

internal sealed class GeminiAiProvider(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiAiProvider> logger) : IAiProvider
{
    public const string HttpClientName = "Gemini";
    private const string ProviderName = "gemini";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonElement ResponseSchema = JsonDocument.Parse(
        """
        {
          "type": "OBJECT",
          "properties": {
            "type": {
              "type": "STRING",
              "enum": ["translation", "question", "answer", "instruction"]
            },
            "original": { "type": "STRING" },
            "translation": { "type": "STRING" },
            "explanation": { "type": "STRING" },
            "suggestedAnswer": { "type": "STRING" },
            "suggestedAnswerTranslation": { "type": "STRING" },
            "questionDetected": { "type": "BOOLEAN" },
            "questionDirectedAtUser": { "type": "BOOLEAN" }
          },
          "required": ["type", "original", "translation", "questionDetected", "questionDirectedAtUser"],
          "propertyOrdering": [
            "type",
            "original",
            "translation",
            "explanation",
            "suggestedAnswer",
            "suggestedAnswerTranslation",
            "questionDetected",
            "questionDirectedAtUser"
          ]
        }
        """).RootElement;

    public string Name => ProviderName;

    public async Task<AiAssistantResponse> ProcessAsync(
        AiConversationRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new AiProviderNotConfiguredException(
                "Gemini API key is not configured. Set Gemini__ApiKey or Gemini:ApiKey in user secrets.");
        }

        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            throw new AiProviderNotConfiguredException("Gemini:Model is not configured.");
        }

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
        {
            throw new AiProviderNotConfiguredException("Gemini:BaseUrl must be an absolute URL.");
        }

        var payload = BuildPayload(request);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var httpRequest = CreateRequest(settings, payload);
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt == 1)
            {
                logger.LogWarning("Gemini returned {StatusCode}. Retrying once.", (int)response.StatusCode);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Gemini request failed with status {StatusCode}. Body length {Length}.",
                    (int)response.StatusCode,
                    body.Length);
                throw new AiProviderException(
                    $"Gemini request failed with status {(int)response.StatusCode}. {Summarize(body)}");
            }

            try
            {
                return GeminiResponseParser.Parse(body);
            }
            catch (AiProviderException exception) when (attempt == 1)
            {
                lastError = exception;
                logger.LogWarning(exception, "Gemini returned an unusable structured result. Retrying once.");
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }

        throw new AiProviderException("Gemini did not return a usable structured result.", lastError);
    }

    private static string BuildPayload(AiConversationRequest request)
    {
        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = GeminiPromptBuilder.BuildSystemInstruction(request) } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = GeminiPromptBuilder.BuildUserPrompt(request) } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1024,
                responseMimeType = "application/json",
                responseSchema = ResponseSchema
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static HttpRequestMessage CreateRequest(GeminiOptions settings, string payload)
    {
        var url =
            $"{settings.BaseUrl.TrimEnd('/')}/v1beta/models/{Uri.EscapeDataString(settings.Model)}:generateContent";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", settings.ApiKey.Trim());
        return request;
    }

    private static string Summarize(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "The response body was empty.";
        }

        var compact = body.ReplaceLineEndings(" ");
        return compact.Length <= 240 ? compact : compact[..240];
    }
}
