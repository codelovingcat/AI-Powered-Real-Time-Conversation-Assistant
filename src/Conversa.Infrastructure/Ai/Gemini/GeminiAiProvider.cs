using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Conversa.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Conversa.Infrastructure.Ai.Gemini;

internal sealed class GeminiAiProvider(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiAiProvider> logger) : IAiProvider
{
    public const string HttpClientName = "Gemini";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Name => "gemini";

    public async Task<AiAssistantResponse> ProcessAsync(
        AiConversationRequest request,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        ValidateSettings(settings);

        var payload = BuildPayload(request);
        var maxAttempts = settings.MaxRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

                using var httpRequest = CreateRequest(settings, payload);
                using var response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var result = GeminiResponseParser.Parse(body);
                        logger.LogInformation(
                            "Gemini request succeeded on attempt {Attempt} in {ElapsedMilliseconds} ms.",
                            attempt,
                            stopwatch.ElapsedMilliseconds);
                        return result;
                    }
                    catch (AiProviderException exception)
                    {
                        logger.LogWarning(
                            "Gemini returned an invalid structured response on attempt {Attempt}. ErrorType {ErrorType}.",
                            attempt,
                            exception.GetType().Name);

                        if (attempt == maxAttempts)
                            throw;
                    }
                }
                else
                {
                    logger.LogWarning(
                        "Gemini request failed with status {StatusCode} on attempt {Attempt} in {ElapsedMilliseconds} ms.",
                        (int)response.StatusCode,
                        attempt,
                        stopwatch.ElapsedMilliseconds);

                    if (!IsTransient(response.StatusCode) || attempt == maxAttempts)
                    {
                        throw new AiProviderException(
                            $"Gemini request failed with status {(int)response.StatusCode}.");
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt <= maxAttempts)
            {
                logger.LogWarning(
                    "Gemini request timed out on attempt {Attempt} after {ElapsedMilliseconds} ms.",
                    attempt,
                    stopwatch.ElapsedMilliseconds);

                if (attempt == maxAttempts)
                    throw new AiProviderException("Gemini request timed out.");
            }
            catch (HttpRequestException exception) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    "Gemini transport error on attempt {Attempt}. ErrorType {ErrorType}.",
                    attempt,
                    exception.GetType().Name);
            }

            if (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(
                    settings.InitialRetryDelayMilliseconds * Math.Pow(2, attempt - 1));

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new AiProviderException("Gemini request failed after all retry attempts.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static void ValidateSettings(GeminiOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new AiProviderNotConfiguredException("Gemini API key is not configured.");

        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new AiProviderNotConfiguredException("Gemini model is not configured.");

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            throw new AiProviderNotConfiguredException("Gemini base URL must be an absolute URL.");
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
                responseMimeType = "application/json"
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
}
