using System.Diagnostics;
using Conversa.Application.Observability;
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

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Name => "gemini";

    public async Task<AiAssistantResponse> ProcessAsync(
        AiConversationRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = ConversaTelemetry.ActivitySource.StartActivity(
            "conversa.ai.process");
        var totalStopwatch = Stopwatch.StartNew();
        var outcome = "error";

        activity?.SetTag("conversa.provider", Name);
        activity?.SetTag("conversa.operation", "ai.process");
        activity?.SetTag("conversa.input_kind", request.InputKind.ToString());

        try
        {
        var settings = options.Value;
        ValidateSettings(settings);

        BoundedGeminiPrompt boundedPrompt;
        try
        {
            boundedPrompt = GeminiPromptBuilder.FitToBudget(request, settings.MaxPromptCharacters);
        }
        catch (GeminiPromptBudgetExceededException exception)
        {
            ConversaTelemetry.AiContextBudgetRejections.Add(
                1,
                ConversaTelemetry.Tags(Name, "context", "rejected"));
            activity?.SetTag("conversa.ai.context_budget_rejected", true);
            throw new AiProviderException(exception.Message);
        }

        var contextTags = ConversaTelemetry.Tags(Name, "context", "bounded");
        ConversaTelemetry.AiPromptCharacters.Record(boundedPrompt.PromptCharacters, contextTags);
        ConversaTelemetry.AiContextTurnsIncluded.Record(boundedPrompt.IncludedTurnCount, contextTags);
        ConversaTelemetry.AiContextTurnsDropped.Record(boundedPrompt.DroppedTurnCount, contextTags);
        ConversaTelemetry.AiContextSummaryIncluded.Record(boundedPrompt.SummaryIncluded ? 1 : 0, contextTags);
        activity?.SetTag("conversa.ai.prompt_characters", boundedPrompt.PromptCharacters);
        activity?.SetTag("conversa.ai.context_turns_included", boundedPrompt.IncludedTurnCount);
        activity?.SetTag("conversa.ai.context_turns_dropped", boundedPrompt.DroppedTurnCount);
        activity?.SetTag("conversa.ai.context_summary_included", boundedPrompt.SummaryIncluded);

        var payload = BuildPayload(boundedPrompt.SystemInstruction, boundedPrompt.UserPrompt);
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
                        outcome = "success";
                        activity?.SetStatus(ActivityStatusCode.Ok);
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
                    var error = ParseGeminiError(response.StatusCode, body);

                    logger.LogWarning(
                        "Gemini request failed with status {StatusCode}, provider error {ProviderErrorCode}, reason {ProviderErrorReason}, quota metric {QuotaMetric} on attempt {Attempt} in {ElapsedMilliseconds} ms.",
                        (int)response.StatusCode,
                        error.Code ?? "unknown",
                        error.Reason ?? "unknown",
                        error.QuotaMetric ?? "unknown",
                        attempt,
                        stopwatch.ElapsedMilliseconds);

                    if (!IsTransient(response.StatusCode, error) || attempt == maxAttempts)
                    {
                        throw new AiProviderException(error.ToSafeMessage());
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
            catch (HttpRequestException exception)
            {
                logger.LogWarning(
                    "Gemini transport error on attempt {Attempt}. ErrorType {ErrorType}.",
                    attempt,
                    exception.GetType().Name);

                if (attempt == maxAttempts)
                    throw new AiProviderException("Gemini transport request failed.", exception);
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
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        finally
        {
            totalStopwatch.Stop();
            ConversaTelemetry.AiRequests.Add(
                1,
                ConversaTelemetry.Tags(Name, "process", outcome));
            ConversaTelemetry.AiDuration.Record(
                totalStopwatch.Elapsed.TotalMilliseconds,
                ConversaTelemetry.Tags(Name, "process", outcome));
            activity?.SetTag("conversa.outcome", outcome);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode, GeminiError error) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout
            || statusCode == HttpStatusCode.TooManyRequests
                && !string.Equals(error.Code, "quota_exceeded", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(error.Reason, "QUOTA_EXCEEDED", StringComparison.OrdinalIgnoreCase);

    private static GeminiError ParseGeminiError(HttpStatusCode statusCode, string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);

            if (!document.RootElement.TryGetProperty("error", out var error))
                return new GeminiError((int)statusCode, null, null, null, null);

            var providerCode = error.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                ? status.GetString()
                : null;

            var machineCode = error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;

            var normalizedCode = !string.IsNullOrWhiteSpace(machineCode)
                ? machineCode
                : null;

            var reason = ExtractErrorReason(error);
            var quotaMetric = ExtractQuotaMetric(error);

            return new GeminiError(
                (int)statusCode,
                normalizedCode ?? providerCode,
                reason,
                quotaMetric,
                providerCode);
        }
        catch (JsonException)
        {
            return new GeminiError((int)statusCode, null, null, null, null);
        }
    }

    private static string? ExtractErrorReason(JsonElement error)
    {
        if (!error.TryGetProperty("details", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var detail in details.EnumerateArray())
        {
            if (detail.TryGetProperty("reason", out var reason)
                && reason.ValueKind == JsonValueKind.String)
            {
                return reason.GetString();
            }
        }

        return null;
    }

    private static string? ExtractQuotaMetric(JsonElement error)
    {
        if (!error.TryGetProperty("details", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var detail in details.EnumerateArray())
        {
            if (!detail.TryGetProperty("metadata", out var metadata)
                || metadata.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (metadata.TryGetProperty("quotaMetric", out var quotaMetric)
                && quotaMetric.ValueKind == JsonValueKind.String)
            {
                return quotaMetric.GetString();
            }
        }

        return null;
    }

    private static void ValidateSettings(GeminiOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new AiProviderNotConfiguredException("Gemini API key is not configured.");

        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new AiProviderNotConfiguredException("Gemini model is not configured.");

        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out _))
            throw new AiProviderNotConfiguredException("Gemini base URL must be an absolute URL.");

        if (settings.MaxPromptCharacters <= 0 || settings.MaxPromptCharacters > 250_000)
            throw new AiProviderNotConfiguredException("Gemini prompt character budget must be between 1 and 250000.");
    }

    private static string BuildPayload(string systemInstruction, string userPrompt)
    {
        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
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

internal sealed record GeminiError(
    int StatusCode,
    string? Code,
    string? Reason,
    string? QuotaMetric,
    string? ProviderStatus)
{
    public string ToSafeMessage()
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Code))
            parts.Add(Code);

        if (!string.IsNullOrWhiteSpace(Reason)
            && !string.Equals(Reason, Code, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(Reason);
        }

        if (!string.IsNullOrWhiteSpace(QuotaMetric))
            parts.Add($"quotaMetric={QuotaMetric}");

        return parts.Count == 0
            ? $"Gemini request failed with status {StatusCode}."
            : $"Gemini request failed with status {StatusCode} ({string.Join(", ", parts)}).";
    }
}
