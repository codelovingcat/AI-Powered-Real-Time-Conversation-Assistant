using Conversa.Domain.Preferences;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conversa.Infrastructure.Persistence.Configurations;

internal sealed class UserAssistantPreferencesConfiguration
    : IEntityTypeConfiguration<UserAssistantPreferences>
{
    public void Configure(EntityTypeBuilder<UserAssistantPreferences> builder)
    {
        builder.ToTable("UserAssistantPreferences", table =>
        {
            table.HasCheckConstraint(
                "CK_UserAssistantPreferences_ResponseLength",
                "\"ResponseLength\" IN ('concise', 'balanced', 'detailed')");
            table.HasCheckConstraint(
                "CK_UserAssistantPreferences_Tone",
                "\"Tone\" IN ('casual', 'contextual', 'formal')");
            table.HasCheckConstraint(
                "CK_UserAssistantPreferences_ExplanationLevel",
                "\"ExplanationLevel\" IN ('minimal', 'balanced', 'detailed')");
        });

        builder.HasKey(preferences => preferences.UserId);

        builder.Property(preferences => preferences.UserId).ValueGeneratedNever();
        builder.Property(preferences => preferences.ResponseLength)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(preferences => preferences.Tone)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(preferences => preferences.ExplanationLevel)
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(preferences => preferences.SuggestReplies).IsRequired();
        builder.Property(preferences => preferences.CreatedAt).IsRequired();
        builder.Property(preferences => preferences.UpdatedAt).IsRequired();
    }
}
