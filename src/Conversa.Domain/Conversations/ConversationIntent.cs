namespace Conversa.Domain.Conversations;

/// <summary>
/// Describes how an utterance relates to the listener, independently of response format.
/// </summary>
public enum ConversationIntent
{
    Statement = 0,
    QuestionDirectedAtUser = 1,
    QuestionDirectedAtOthers = 2,
    RhetoricalQuestion = 3,
    RequestDirectedAtUser = 4,
    RequestDirectedAtOthers = 5,
    UserFormulationRequest = 6,
    Ambiguous = 7
}
