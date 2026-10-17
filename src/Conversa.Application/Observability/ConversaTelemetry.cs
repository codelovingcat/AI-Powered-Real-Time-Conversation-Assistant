using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Conversa.Application.Observability;

public static class ConversaTelemetry
{
    public const string ActivitySourceName = "Conversa";
    public const string MeterName = "Conversa";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> AiRequests =
        Meter.CreateCounter<long>(
            "conversa.ai.requests",
            unit: "{request}",
            description: "Number of AI provider operations by outcome.");

    public static readonly Histogram<double> AiDuration =
        Meter.CreateHistogram<double>(
            "conversa.ai.duration",
            unit: "ms",
            description: "AI provider operation duration.");

    public static readonly Histogram<double> AiPromptCharacters =
        Meter.CreateHistogram<double>(
            "conversa.ai.prompt.characters",
            unit: "{character}",
            description: "Character count of the bounded system instruction and user prompt.");

    public static readonly Histogram<double> AiContextTurnsIncluded =
        Meter.CreateHistogram<double>(
            "conversa.ai.context.turns.included",
            unit: "{turn}",
            description: "Number of historical turns included in the bounded AI prompt.");

    public static readonly Histogram<double> AiContextTurnsDropped =
        Meter.CreateHistogram<double>(
            "conversa.ai.context.turns.dropped",
            unit: "{turn}",
            description: "Number of oldest historical turns omitted to fit the AI prompt budget.");

    public static readonly Histogram<double> AiContextSummaryIncluded =
        Meter.CreateHistogram<double>(
            "conversa.ai.context.summary_included",
            unit: "{boolean}",
            description: "Whether the persisted older-context summary was included in the bounded AI prompt (1 or 0).");

    public static readonly Counter<long> AiContextBudgetRejections =
        Meter.CreateCounter<long>(
            "conversa.ai.context.budget_rejections",
            unit: "{request}",
            description: "Requests rejected because instruction and latest input alone exceed the prompt budget.");

    public static readonly Counter<long> SttRequests =
        Meter.CreateCounter<long>(
            "conversa.stt.requests",
            unit: "{request}",
            description: "Number of speech-to-text provider operations by outcome.");

    public static readonly Histogram<double> SttDuration =
        Meter.CreateHistogram<double>(
            "conversa.stt.duration",
            unit: "ms",
            description: "Speech-to-text provider operation duration.");

    public static readonly UpDownCounter<long> WebSocketConnections =
        Meter.CreateUpDownCounter<long>(
            "conversa.websocket.connections.active",
            unit: "{connection}",
            description: "Number of active conversation audio WebSocket connections.");

    public static readonly Histogram<double> WebSocketDuration =
        Meter.CreateHistogram<double>(
            "conversa.websocket.duration",
            unit: "ms",
            description: "Conversation audio WebSocket connection duration.");

    public static readonly Counter<long> WebSocketFailures =
        Meter.CreateCounter<long>(
            "conversa.websocket.failures",
            unit: "{failure}",
            description: "Number of conversation audio WebSocket failures.");

    public static TagList Tags(string provider, string operation, string outcome) =>
        new()
        {
            { "provider", provider },
            { "operation", operation },
            { "outcome", outcome }
        };
}

