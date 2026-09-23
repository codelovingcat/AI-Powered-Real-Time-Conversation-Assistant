using Conversa.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conversa.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(message => message.OriginalText)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.Translation).HasColumnType("text");
        builder.Property(message => message.Explanation).HasColumnType("text");
        builder.Property(message => message.SuggestedAnswer).HasColumnType("text");
        builder.Property(message => message.SuggestedAnswerTranslation).HasColumnType("text");

        builder.Property(message => message.ResponseType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(message => message.QuestionDetected)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(message => message.QuestionDirectedAtUser)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(message => message.ProviderName)
            .HasMaxLength(Message.ProviderNameMaxLength);

        builder.Property(message => message.CreatedAt).IsRequired();

        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt });
    }
}
