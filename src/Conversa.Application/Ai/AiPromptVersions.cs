namespace Conversa.Application.Ai;

/// <summary>
/// Version identifiers are intentionally low-cardinality and non-sensitive.
/// Bump the matching value whenever that prompt's system instructions, user prompt
/// composition, structured-output expectations, or safety semantics change.
/// </summary>
public static class AiPromptVersions
{
    public const string Assistant = "assistant-v1";
    public const string ContextSummary = "context-summary-v1";
}
