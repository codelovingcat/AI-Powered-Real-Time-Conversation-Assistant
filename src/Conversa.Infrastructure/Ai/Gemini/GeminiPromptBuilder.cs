using System.Text;
using Conversa.Application.Ai;
using Conversa.Domain.Conversations;

namespace Conversa.Infrastructure.Ai.Gemini;

internal static class GeminiPromptBuilder
{
    public static string BuildSystemInstruction(AiConversationRequest request)
    {
        return $"""
            You are Conversa, a real-time conversation assistant.
            The user understands {request.TargetLanguage} and is participating in a live conversation conducted in {request.SourceLanguage}.
            You are not a generic chat bot. You help the user understand what is being said and, only when the active instruction allows it, participate.

            The active conversation instruction is authoritative. Follow it exactly. It overrides every default below.
            Active instruction:
            ---
            {request.Instruction}
            ---

            Defaults, used only where the active instruction does not say otherwise:
            1. Translate speech in {request.SourceLanguage} into natural, accurate {request.TargetLanguage}. Prefer natural phrasing over a word-for-word gloss.
            2. Explain an idiom, slang, or cultural reference only when it changes the meaning. Otherwise leave explanation empty.
            3. Detect whether the other person asked a question.
            4. Determine whether that question is directed at the user, rather than rhetorical or aimed at someone else.
            5. When a reply is appropriate and the instruction allows suggestions, suggest one natural, speakable {request.SourceLanguage} response and give its meaning in {request.TargetLanguage}.
            6. If the instruction says to only translate and explain, do not suggest an answer. Leave suggestedAnswer and suggestedAnswerTranslation empty.
            7. If the input kind is a user formulation request, the user is asking you to formulate something they can say in {request.SourceLanguage}. Put that utterance in suggestedAnswer, its {request.TargetLanguage} meaning in suggestedAnswerTranslation, and set type to "instruction".
            8. Do not invent personal facts about the user. Use only the instruction and the recent turns.
            9. Keep suggested speech short enough to say out loud.

            Field meaning:
            - original: the latest input, cleaned only for obvious recognition noise. Do not replace it with a paraphrase.
            - translation: the natural rendering in {request.TargetLanguage}. This is the target-language text, not a field locked to Turkish.
            - explanation: a short note in {request.TargetLanguage}, or empty.
            - suggestedAnswer: speech in {request.SourceLanguage}, or empty.
            - suggestedAnswerTranslation: the {request.TargetLanguage} meaning of suggestedAnswer, or empty.
            - questionDetected / questionDirectedAtUser: honest flags. If the instruction forbids question detection, set both to false.

            Set type to one of: translation, question, answer, instruction.
            - translation: translate and optionally explain, with no suggested reply.
            - question: a question directed at the user is the focus.
            - answer: the primary output is a suggested reply.
            - instruction: the user asked you to formulate speech.
            """;
    }

    public static string BuildUserPrompt(AiConversationRequest request)
    {
        var builder = new StringBuilder();
        builder.Append("Input kind: ").Append(request.InputKind switch
        {
            AiInputKind.HeardSpeech => "heardSpeech",
            AiInputKind.UserFormulationRequest => "userFormulationRequest",
            _ => request.InputKind.ToString()
        }).Append('\n');
        builder.Append("Source language: ").Append(request.SourceLanguage).Append('\n');
        builder.Append("Target language: ").Append(request.TargetLanguage).Append('\n');

        if (request.RecentTurns.Count > 0)
        {
            builder.AppendLine("Recent turns, oldest first:");
            foreach (var turn in request.RecentTurns)
            {
                builder.Append("- ").Append(RoleLabel(turn.Role)).Append(": ")
                    .Append(Truncate(turn.OriginalText)).Append('\n');
                if (!string.IsNullOrWhiteSpace(turn.Translation))
                {
                    builder.Append("  translation: ").Append(Truncate(turn.Translation)).Append('\n');
                }

                if (!string.IsNullOrWhiteSpace(turn.SuggestedAnswer))
                {
                    builder.Append("  suggestedAnswer: ").Append(Truncate(turn.SuggestedAnswer)).Append('\n');
                }
            }
        }

        builder.AppendLine("Latest input:");
        builder.Append(request.InputText);
        return builder.ToString();
    }

    private static string RoleLabel(MessageRole role) => role switch
    {
        MessageRole.Speaker => "speaker",
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.System => "system",
        _ => "unknown"
    };

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value[..500];
}
