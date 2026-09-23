using Conversa.Domain.Conversations;

namespace Conversa.Application.Ai;

/// <summary>
/// Structured assistant result. <see cref="Translation"/> is the rendering in the
/// conversation target language (Turkish in the initial scenario). Property names stay
/// language-neutral so additional language pairs do not require a new model.
/// </summary>
public sealed record AiAssistantResponse(
    AiResponseType Type,
    string Original,
    string? Translation,
    string? Explanation,
    string? SuggestedAnswer,
    string? SuggestedAnswerTranslation,
    bool QuestionDetected,
    bool QuestionDirectedAtUser);
