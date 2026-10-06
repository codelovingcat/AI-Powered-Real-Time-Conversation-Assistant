using System.Diagnostics;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Application.Observability;
using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationAssistantFlowTests
{
    [Fact]
    public async Task ProcessAsync_BuildsContext_CallsAiProvider_AndPersistsResult()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ConversaTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(
            userId,
            "English conversation",
            "Translate naturally into Turkish, explain useful context, and suggest a reply when the speaker asks me a question.",
            "en",
            "tr",
            now);

        var history = Message.CreateFromAssistant(
            conversation.Id,
            MessageRole.Speaker,
            "How was your weekend?",
            "Hafta sonun nasıldı?",
            null,
            null,
            null,
            AiResponseType.Question,
            questionDetected: true,
            questionDirectedAtUser: true,
            "gemini",
            now.AddMinutes(-1));

        var repository = new TestConversationRepository(conversation);
        var messages = new TestMessageRepository(history);
        var unitOfWork = new RecordingUnitOfWork();
        var aiProvider = new RecordingAiProvider(new AiAssistantResponse(
            AiResponseType.Question,
            "Would you like some coffee?",
            "Bir kahve ister misin?",
            "The speaker is directly asking whether you want coffee.",
            "Sure, I'd love some.",
            "Tabii, memnuniyetle.",
            QuestionDetected: true,
            QuestionDirectedAtUser: true));

        var assistant = new ConversationAssistant(
            new TestCurrentUser(userId),
            repository,
            messages,
            aiProvider,
            unitOfWork,
            TimeProvider.System);

        var result = await assistant.ProcessAsync(
            new ProcessConversationInputCommand(
                conversation.Id,
                AiInputKind.HeardSpeech,
                "  Would you like some coffee?  "),
            CancellationToken.None);

        Assert.Equal("gemini", result.ProviderName);
        Assert.Equal(MessageRole.Speaker, result.Role);
        Assert.Equal("Would you like some coffee?", result.OriginalText);
        Assert.Equal("Bir kahve ister misin?", result.Translation);
        Assert.Equal(AiResponseType.Question, result.ResponseType);
        Assert.True(result.QuestionDetected);
        Assert.True(result.QuestionDirectedAtUser);
        Assert.Equal("Sure, I'd love some.", result.SuggestedAnswer);
        Assert.Equal("Tabii, memnuniyetle.", result.SuggestedAnswerTranslation);

        var request = Assert.Single(aiProvider.Requests);
        Assert.Equal(conversation.Instruction, request.Instruction);
        Assert.Equal("en", request.SourceLanguage);
        Assert.Equal("tr", request.TargetLanguage);
        Assert.Equal(AiInputKind.HeardSpeech, request.InputKind);
        Assert.Equal("Would you like some coffee?", request.InputText);

        var turn = Assert.Single(request.RecentTurns);
        Assert.Equal(MessageRole.Speaker, turn.Role);
        Assert.Equal("How was your weekend?", turn.OriginalText);
        Assert.Equal("Hafta sonun nasıldı?", turn.Translation);

        var persisted = Assert.Single(messages.AddedMessages);
        Assert.Equal(result.Id, persisted.Id);
        Assert.Equal(conversation.Id, persisted.ConversationId);
        Assert.Equal(MessageRole.Speaker, persisted.Role);
        Assert.Equal("gemini", persisted.ProviderName);
        Assert.Equal(1, unitOfWork.SaveCalls);
        Assert.True(conversation.UpdatedAt >= now);

        var telemetryActivity = Assert.Single(
            activities,
            activity => activity.OperationName == "conversa.assistant.process");
        Assert.Equal(ActivityStatusCode.Ok, telemetryActivity.Status);
        Assert.Equal("success", telemetryActivity.GetTagItem("conversa.outcome"));
        Assert.Equal(AiInputKind.HeardSpeech.ToString(), telemetryActivity.GetTagItem("conversa.input_kind"));
        Assert.DoesNotContain(
            telemetryActivity.TagObjects,
            tag => tag.Key.Contains("text", StringComparison.OrdinalIgnoreCase)
                || tag.Key.Contains("instruction", StringComparison.OrdinalIgnoreCase)
                || tag.Key.Contains("conversation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProcessAsync_UserFormulationRequest_PersistsAsUserMessage()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(
            userId,
            "Formulation",
            "Help me formulate natural English responses. Do not translate the user's Turkish text as a speaker turn.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);

        var messages = new TestMessageRepository();
        var aiProvider = new RecordingAiProvider(new AiAssistantResponse(
            AiResponseType.Instruction,
            "Geç kaldığım için üzgünüm.",
            null,
            null,
            "I'm sorry I'm late.",
            "Geç kaldığım için üzgünüm.",
            QuestionDetected: false,
            QuestionDirectedAtUser: false));

        var assistant = new ConversationAssistant(
            new TestCurrentUser(userId),
            new TestConversationRepository(conversation),
            messages,
            aiProvider,
            new RecordingUnitOfWork(),
            TimeProvider.System);

        var result = await assistant.ProcessAsync(
            new ProcessConversationInputCommand(
                conversation.Id,
                AiInputKind.UserFormulationRequest,
                "Geç kaldığım için üzgünüm."),
            CancellationToken.None);

        Assert.Equal(MessageRole.User, result.Role);
        Assert.Equal(AiResponseType.Instruction, result.ResponseType);
        Assert.Equal("I'm sorry I'm late.", result.SuggestedAnswer);

        var persisted = Assert.Single(messages.AddedMessages);
        Assert.Equal(MessageRole.User, persisted.Role);
        Assert.Equal("geç kaldığım için üzgünüm.".ToUpperInvariant(), persisted.OriginalText.ToUpperInvariant());
    }

    [Fact]
    public async Task ProcessAsync_WhenAiProviderFails_DoesNotPersistMessage()
    {
        var userId = Guid.NewGuid();
        var conversation = Conversation.Create(
            userId,
            "Failure path",
            "Translate naturally.",
            "en",
            "tr",
            DateTimeOffset.UtcNow);

        var messages = new TestMessageRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var assistant = new ConversationAssistant(
            new TestCurrentUser(userId),
            new TestConversationRepository(conversation),
            messages,
            new ThrowingAiProvider(),
            unitOfWork,
            TimeProvider.System);

        await Assert.ThrowsAsync<AiProviderException>(() =>
            assistant.ProcessAsync(
                new ProcessConversationInputCommand(
                    conversation.Id,
                    AiInputKind.HeardSpeech,
                    "Hello there."),
                CancellationToken.None));

        Assert.Empty(messages.AddedMessages);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class TestConversationRepository(Conversation conversation) : IConversationRepository
    {
        public Task<Conversation?> GetByIdAsync(
            Guid id,
            Guid userId,
            CancellationToken cancellationToken)
            => Task.FromResult<Conversation?>(
                id == conversation.Id && userId == conversation.UserId
                    ? conversation
                    : null);

        public Task<IReadOnlyList<Conversation>> ListByUserAsync(
            Guid userId,
            int take,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Remove(Conversation conversation)
            => throw new NotSupportedException();
    }

    private sealed class TestMessageRepository(params Message[] initialMessages) : IMessageRepository
    {
        private readonly List<Message> _messages = [.. initialMessages];

        public List<Message> AddedMessages { get; } = [];

        public Task<IReadOnlyList<Conversation>> ListByUserPageAsync(
            Guid userId,
            int take,
            DateTimeOffset? beforeUpdatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Message>> ListByConversationAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Message>>(
                _messages
                    .Where(message => message.ConversationId == conversationId)
                    .OrderBy(message => message.CreatedAt)
                    .Take(take)
                    .ToArray());

        public Task<IReadOnlyList<Message>> ListByConversationPageAsync(
            Guid conversationId,
            int take,
            DateTimeOffset? beforeCreatedAt,
            Guid? beforeId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Message>> ListRecentAsync(
            Guid conversationId,
            int take,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Message>>(
                _messages
                    .Where(message => message.ConversationId == conversationId)
                    .OrderByDescending(message => message.CreatedAt)
                    .Take(take)
                    .Reverse()
                    .ToArray());

        public Task AddAsync(Message message, CancellationToken cancellationToken)
        {
            _messages.Add(message);
            AddedMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAiProvider(AiAssistantResponse response) : IAiProvider
    {
        public string Name => "gemini";

        public List<AiConversationRequest> Requests { get; } = [];

        public Task<AiAssistantResponse> ProcessAsync(
            AiConversationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingAiProvider : IAiProvider
    {
        public string Name => "gemini";

        public Task<AiAssistantResponse> ProcessAsync(
            AiConversationRequest request,
            CancellationToken cancellationToken)
            => throw new AiProviderException("Simulated provider failure.");
    }
}
