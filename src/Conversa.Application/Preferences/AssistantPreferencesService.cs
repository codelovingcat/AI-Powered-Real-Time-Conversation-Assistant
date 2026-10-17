using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Common;
using Conversa.Domain.Preferences;
using Conversa.Application.Ai;

namespace Conversa.Application.Preferences;

public sealed record AssistantPreferencesDto(
    string ResponseLength,
    string Tone,
    string ExplanationLevel,
    bool SuggestReplies)
{
    public static AssistantPreferencesDto Default { get; } = new(
        AssistantPreferenceValues.DefaultResponseLength,
        AssistantPreferenceValues.DefaultTone,
        AssistantPreferenceValues.DefaultExplanationLevel,
        AssistantPreferenceValues.DefaultSuggestReplies);
}

public sealed record UpdateAssistantPreferencesCommand(
    string ResponseLength,
    string Tone,
    string ExplanationLevel,
    bool SuggestReplies);

public interface IAssistantPreferencesService
{
    Task<AssistantPreferencesDto> GetAsync(CancellationToken cancellationToken);

    Task<AssistantPreferencesDto> UpdateAsync(
        UpdateAssistantPreferencesCommand command,
        CancellationToken cancellationToken);
}

public sealed class AssistantPreferencesService(
    ICurrentUser currentUser,
    IUserAssistantPreferencesRepository preferencesRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAssistantPreferencesService
{
    public async Task<AssistantPreferencesDto> GetAsync(CancellationToken cancellationToken)
    {
        var preferences = await preferencesRepository.GetByUserIdAsync(
            currentUser.UserId,
            cancellationToken);

        return preferences is null ? AssistantPreferencesDto.Default : ToDto(preferences);
    }

    public async Task<AssistantPreferencesDto> UpdateAsync(
        UpdateAssistantPreferencesCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw ValidationException.For("userId", "An authenticated user is required.");
        }

        if (!AssistantPreferenceValues.IsValidResponseLength(command.ResponseLength))
        {
            throw ValidationException.For(
                "responseLength",
                "responseLength must be concise, balanced, or detailed.");
        }

        if (!AssistantPreferenceValues.IsValidTone(command.Tone))
        {
            throw ValidationException.For(
                "tone",
                "tone must be casual, contextual, or formal.");
        }

        if (!AssistantPreferenceValues.IsValidExplanationLevel(command.ExplanationLevel))
        {
            throw ValidationException.For(
                "explanationLevel",
                "explanationLevel must be minimal, balanced, or detailed.");
        }

        var now = timeProvider.GetUtcNow();
        var preferences = await preferencesRepository.GetByUserIdAsync(
            currentUser.UserId,
            cancellationToken);

        if (preferences is null)
        {
            preferences = UserAssistantPreferences.Create(
                currentUser.UserId,
                command.ResponseLength,
                command.Tone,
                command.ExplanationLevel,
                command.SuggestReplies,
                now);
            await preferencesRepository.AddAsync(preferences, cancellationToken);
        }
        else
        {
            preferences.Update(
                command.ResponseLength,
                command.Tone,
                command.ExplanationLevel,
                command.SuggestReplies,
                now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(preferences);
    }

    private static AssistantPreferencesDto ToDto(UserAssistantPreferences preferences)
        => new(
            preferences.ResponseLength,
            preferences.Tone,
            preferences.ExplanationLevel,
            preferences.SuggestReplies);

    public static AssistantResponsePreferences ToResponsePreferences(
        AssistantPreferencesDto preferences)
        => new(
            preferences.ResponseLength,
            preferences.Tone,
            preferences.ExplanationLevel,
            preferences.SuggestReplies);
}
