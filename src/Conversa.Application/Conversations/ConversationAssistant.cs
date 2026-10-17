using System.Diagnostics;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Application.Observability;
using Conversa.Domain.Conversations;
using Conversa.Domain.Preferences;

namespace Conversa.Application.Conversations;

public sealed class ConversationAssistant(
    ICurrentUser currentUser,
    IConversationRepository conversations,
    IMessageRepository messages,
    IAiProvider aiProvider,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IConversationContextService? contextService = null,
    IUserAssistantPreferencesRepository? preferencesRepository = null) : IConversationAssistant
{
    public const int ContextMessageCount = 12;

    public async Task<MessageDto> ProcessAsync(
        ProcessConversationInputCommand command,
        CancellationToken cancellationToken)
    {
        using var activity = ConversaTelemetry.ActivitySource.StartActivity(
            "conversa.assistant.process");
        var stopwatch = Stopwatch.StartNew();
        var outcome = "error";
        activity?.SetTag("conversa.operation", "assistant");
        activity?.SetTag("conversa.input_kind", command.InputKind.ToString());

        try
        {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            throw ValidationException.For("text", "Text is required.");
        }

        if (command.Text.Trim().Length > Message.TextMaxLength)
        {
            throw ValidationException.For("text", $"Text cannot exceed {Message.TextMaxLength} characters.");
        }

        var conversation = await conversations.GetByIdAsync(
            command.ConversationId,
            currentUser.UserId,
            cancellationToken);

        if (conversation is null)
        {
            throw new NotFoundException("Conversation was not found.");
        }

        var recent = await messages.ListRecentAsync(conversation.Id, ContextMessageCount, cancellationToken);
        var contextSummary = contextService is null
            ? null
            : await contextService.PrepareContextSummaryAsync(conversation, recent, cancellationToken);
        var storedPreferences = preferencesRepository is null
            ? null
            : await preferencesRepository.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        var responsePreferences = storedPreferences is null
            ? AssistantResponsePreferences.Default
            : new AssistantResponsePreferences(
                storedPreferences.ResponseLength,
                storedPreferences.Tone,
                storedPreferences.ExplanationLevel,
                storedPreferences.SuggestReplies);
        var request = new AiConversationRequest(
            conversation.Instruction,
            conversation.SourceLanguage,
            conversation.TargetLanguage,
            command.Text.Trim(),
            command.InputKind,
            recent.Select(message => new AiConversationTurn(
                message.Role,
                message.OriginalText,
                message.Translation,
                message.SuggestedAnswer,
                message.CreatedAt)).ToArray(),
            contextSummary,
            responsePreferences);

        var aiResponse = await aiProvider.ProcessAsync(request, cancellationToken);
        var result = AiAssistantResponsePreferencesPolicy.Apply(
            aiResponse,
            command.InputKind,
            responsePreferences);
        var now = timeProvider.GetUtcNow();
        var role = command.InputKind == AiInputKind.UserFormulationRequest
            ? MessageRole.User
            : MessageRole.Speaker;

        Message message;
        try
        {
            message = Message.CreateFromAssistant(
                conversation.Id,
                role,
                result.Original,
                result.Translation,
                result.Explanation,
                result.SuggestedAnswer,
                result.SuggestedAnswerTranslation,
                result.Type,
                result.QuestionDetected,
                result.QuestionDirectedAtUser,
                aiProvider.Name,
                now,
                result.PromptVersion,
                result.Intent);
        }
        catch (ArgumentException exception)
        {
            throw new AiProviderException("The AI provider returned a result that could not be stored.", exception);
        }

        conversation.Touch(now);
        await messages.AddAsync(message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        outcome = "success";
        activity?.SetStatus(ActivityStatusCode.Ok);
        return message.ToDto();
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
            stopwatch.Stop();
            activity?.SetTag("conversa.outcome", outcome);
        }
    }
}
