using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conversa.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations", table =>
        {
            table.HasCheckConstraint(
                "CK_Conversations_LifecycleState",
                "\"LifecycleState\" IN ('Ready', 'Active', 'Paused', 'Completed')");
        });
        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.Title)
            .HasMaxLength(Conversation.TitleMaxLength)
            .IsRequired();

        builder.Property(conversation => conversation.Instruction)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(conversation => conversation.SourceLanguage)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(conversation => conversation.TargetLanguage)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(conversation => conversation.CreatedAt).IsRequired();
        builder.Property(conversation => conversation.UpdatedAt).IsRequired();

        builder.Property(conversation => conversation.LifecycleState)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(ConversationLifecycleState.Active)
            .IsRequired();

        builder.Property(conversation => conversation.StartedAt);
        builder.Property(conversation => conversation.CompletedAt);

        builder.HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAt });

        builder.HasMany(conversation => conversation.Messages)
            .WithOne()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(conversation => conversation.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
