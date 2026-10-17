using System.Diagnostics;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Application.Observability;
using Conversa.Domain.Conversations;
using Microsoft.Extensions.Logging;

namespace Conversa.Application.Conversations;

public sealed class ConversationAssistant(
    ICurrentUser currentUser,
    IConversationRepository conversations,
    IMessageRepository messages,
    IAiProvider aiProvider,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IAiConversationSummarizer? summarizer = null,
    ILogger<ConversationAssistant>? logger = null) : IConversationAssistant
{
    public const int ContextMessageCount = 12;
    private const int SummaryBatchMessageCount = 8;
    private const int MinimumSummaryMessageCount = 8;

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
            conversation.ContextSummary);

        var result = await aiProvider.ProcessAsync(request, cancellationToken);
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
                now);
        }
        catch (ArgumentException exception)
        {
            throw new AiProviderException("The AI provider returned a result that could not be stored.", exception);
        }

        conversation.Touch(now);
        await messages.AddAsync(message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Summary refresh is best-effort and happens after the user-visible result
        // has been committed. A provider/database failure cannot roll it back or hide it.
        await RefreshContextSummaryAsync(conversation, cancellationToken);

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
    private async Task RefreshContextSummaryAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        if (summarizer is null)
            return;

        try
        {
            var candidates = await messages.ListSummaryCandidatesAsync(
                conversation.Id,
                ContextMessageCount,
                conversation.ContextSummaryThroughCreatedAt,
                conversation.ContextSummaryThroughMessageId,
                SummaryBatchMessageCount,
                cancellationToken);

            if (candidates.Count < MinimumSummaryMessageCount)
                return;

            var olderTurns = candidates.Select(message => new AiConversationTurn(
                message.Role,
                message.OriginalText,
                message.Translation,
                message.SuggestedAnswer,
                message.CreatedAt)).ToArray();

            var summary = await summarizer.SummarizeAsync(
                conversation.Instruction,
                conversation.SourceLanguage,
                conversation.TargetLanguage,
                conversation.ContextSummary,
                olderTurns,
                cancellationToken);

            if (!conversation.UpdateContextSummary(
                summary,
                candidates[^1].Id,
                candidates[^1].CreatedAt,
                timeProvider.GetUtcNow()))
            {
                return;
            }

            // The revision column is an optimistic concurrency token. If another
            // request won the race, this save fails and the next turn can retry
            // from the winning checkpoint without losing already saved messages.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Never include the prompt, transcript, summary or provider body in logs.
            logger?.LogWarning(
                "Conversation context summary refresh failed. ErrorType {ErrorType}.",
                exception.GetType().Name);
        }
    }
}
