using Conversa.Domain.Conversations;

namespace Conversa.Api.Tests.Conversations;

public sealed class ConversationContextSummaryTests
{
    [Fact]
    public void UpdateContextSummary_AdvancesCheckpointButNeverMovesItBackward()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(
            Guid.NewGuid(),
            "Summary checkpoint",
            "Translate naturally.",
            "en",
            "tr",
            now);
        var firstId = Guid.NewGuid();

        Assert.True(conversation.UpdateContextSummary(
            "First summary.",
            firstId,
            now,
            now.AddSeconds(1)));
        Assert.Equal(1, conversation.ContextSummaryRevision);
        Assert.Equal(firstId, conversation.ContextSummaryThroughMessageId);

        Assert.False(conversation.UpdateContextSummary(
            "Stale summary.",
            Guid.NewGuid(),
            now.AddMinutes(-1),
            now.AddSeconds(2)));

        Assert.Equal("First summary.", conversation.ContextSummary);
        Assert.Equal(1, conversation.ContextSummaryRevision);
    }

    [Fact]
    public void UpdateContextSummary_RejectsOversizedSummaryWithoutChangingState()
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Create(
            Guid.NewGuid(),
            "Summary bounds",
            "Translate naturally.",
            "en",
            "tr",
            now);

        Assert.Throws<ArgumentException>(() => conversation.UpdateContextSummary(
            new string('x', Conversation.ContextSummaryMaxLength + 1),
            Guid.NewGuid(),
            now,
            now));

        Assert.Null(conversation.ContextSummary);
        Assert.Null(conversation.ContextSummaryThroughMessageId);
        Assert.Equal(0, conversation.ContextSummaryRevision);
    }
}
