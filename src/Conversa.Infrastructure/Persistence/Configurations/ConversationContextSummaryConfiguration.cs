using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conversa.Infrastructure.Persistence.Configurations;

internal sealed class ConversationContextSummaryConfiguration
    : IEntityTypeConfiguration<ConversationContextSummary>
{
    public void Configure(EntityTypeBuilder<ConversationContextSummary> builder)
    {
        builder.ToTable("ConversationContextSummaries");
        builder.HasKey(summary => summary.ConversationId);

        builder.Property(summary => summary.Summary)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(summary => summary.PromptVersion)
            .HasMaxLength(ConversationContextSummary.PromptVersionMaxLength)
            .IsRequired();

        builder.Property(summary => summary.ThroughCreatedAt).IsRequired();
        builder.Property(summary => summary.ThroughMessageId).IsRequired();
        builder.Property(summary => summary.UpdatedAt).IsRequired();
        builder.Property(summary => summary.Revision)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(summary => summary.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
