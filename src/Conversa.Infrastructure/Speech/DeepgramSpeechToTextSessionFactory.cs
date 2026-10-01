using Conversa.Application.Speech;
using Microsoft.Extensions.Options;

namespace Conversa.Infrastructure.Speech;

public sealed class DeepgramSpeechToTextSessionFactory(
    IOptions<DeepgramOptions> options)
    : ISpeechToTextSessionFactory
{
    public string ProviderName => "deepgram";

    public async Task<ISpeechToTextSession> OpenSessionAsync(
        SpeechSessionOptions sessionOptions,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        settings.Validate();

        if (sessionOptions.ConversationId == Guid.Empty)
        {
            throw new ArgumentException(
                "ConversationId must not be empty.",
                nameof(sessionOptions));
        }

        if (string.IsNullOrWhiteSpace(sessionOptions.Language))
        {
            throw new ArgumentException(
                "Language must be configured.",
                nameof(sessionOptions));
        }

        var session = new DeepgramSpeechToTextSession(settings, sessionOptions);
        try
        {
            await session.StartAsync(cancellationToken);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}
