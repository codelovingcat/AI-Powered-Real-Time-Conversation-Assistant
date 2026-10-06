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

