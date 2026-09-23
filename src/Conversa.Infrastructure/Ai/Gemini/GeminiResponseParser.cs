using System.Text.Json;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Domain.Conversations;

namespace Conversa.Infrastructure.Ai.Gemini;

internal static class GeminiResponseParser
{
    public static AiAssistantResponse Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var text = ExtractCandidateText(document.RootElement);
            return ParseModelJson(text);
        }
        catch (AiProviderException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new AiProviderException("Gemini returned a response that was not valid JSON.", exception);
        }
    }

    public static AiAssistantResponse ParseModelJson(string modelJson)
    {
        var cleaned = StripCodeFence(modelJson);
        using var document = JsonDocument.Parse(cleaned);
        var root = document.RootElement;

        var type = ParseType(RequiredString(root, "type"));
        var original = RequiredString(root, "original");
        var translation = OptionalString(root, "translation");
        var explanation = OptionalString(root, "explanation");
        var suggestedAnswer = OptionalString(root, "suggestedAnswer");
        var suggestedAnswerTranslation = OptionalString(root, "suggestedAnswerTranslation");
        var questionDetected = OptionalBool(root, "questionDetected");
        var questionDirectedAtUser = OptionalBool(root, "questionDirectedAtUser");

        if (string.IsNullOrWhiteSpace(translation) && string.IsNullOrWhiteSpace(suggestedAnswer))
        {
            throw new AiProviderException("Gemini returned neither a translation nor a suggested answer.");
        }

        return new AiAssistantResponse(
            type,
            original,
            translation,
            explanation,
            suggestedAnswer,
            suggestedAnswerTranslation,
            questionDetected,
            questionDirectedAtUser);
    }

    private static string ExtractCandidateText(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback)
            && feedback.TryGetProperty("blockReason", out var blockReason)
            && blockReason.ValueKind == JsonValueKind.String)
        {
            throw new AiProviderException($"Gemini blocked the prompt: {blockReason.GetString()}.");
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            throw new AiProviderException("Gemini returned no candidates.");
        }

        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var finishReason)
            && finishReason.ValueKind == JsonValueKind.String
            && string.Equals(finishReason.GetString(), "SAFETY", StringComparison.OrdinalIgnoreCase))
        {
            throw new AiProviderException("Gemini stopped because of a safety filter.");
        }

        if (!candidate.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts))
        {
            throw new AiProviderException("Gemini returned a candidate without content.");
        }

        var builder = new System.Text.StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        var combined = builder.ToString();
        if (string.IsNullOrWhiteSpace(combined))
        {
            throw new AiProviderException("Gemini returned an empty candidate.");
        }

        return combined;
    }

    private static AiResponseType ParseType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "translation" => AiResponseType.Translation,
        "question" => AiResponseType.Question,
        "answer" => AiResponseType.Answer,
        "instruction" => AiResponseType.Instruction,
        _ => throw new AiProviderException($"Gemini returned an unknown response type '{value}'.")
    };

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            throw new AiProviderException($"Gemini omitted required field '{name}'.");
        }

        var value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AiProviderException($"Gemini returned an empty '{name}' field.");
        }

        return value.Trim();
    }

    private static string? OptionalString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new AiProviderException($"Gemini field '{name}' was not a string.");
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool OptionalBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new AiProviderException($"Gemini field '{name}' was not a boolean.")
        };
    }

    private static string StripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0)
        {
            return trimmed;
        }

        var body = trimmed[(firstNewline + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? body[..closing].Trim() : body.Trim();
    }
}
