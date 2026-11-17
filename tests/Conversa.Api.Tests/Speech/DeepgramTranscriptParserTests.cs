using Conversa.Application.Speech;
using Conversa.Infrastructure.Speech;

namespace Conversa.Api.Tests.Speech;

public sealed class DeepgramTranscriptParserTests
{
    [Fact]
    public void ParseStreamingResult_reads_partial_transcript()
    {
        const string json = """
        {
          "is_final": false,
          "speech_final": false,
          "channel": {
            "alternatives": [
              {
                "transcript": "Hello there",
                "confidence": 0.91
              }
            ]
          }
        }
        """;

        var result = DeepgramTranscriptParser.ParseStreamingResult(json);

        Assert.NotNull(result);
        Assert.Equal("Hello there", result.Text);
        Assert.False(result.IsFinal);
        Assert.Equal(0.91f, result.Confidence);
        Assert.Equal(SpeechTranscriptStatus.Transcript, result.Status);
    }

    [Fact]
    public void ParseStreamingResult_marks_speech_final_as_completed()
    {
        const string json = """
        {
          "is_final": true,
          "speech_final": true,
          "channel": {
            "alternatives": [
              {
                "transcript": "Hello there.",
                "confidence": 0.98
              }
            ]
          }
        }
        """;

        var result = DeepgramTranscriptParser.ParseStreamingResult(json);

        Assert.NotNull(result);
        Assert.True(result.IsFinal);
        Assert.Equal(SpeechTranscriptStatus.Completed, result.Status);
    }

    [Fact]
    public void ParsePreRecordedResult_reads_first_channel_alternative()
    {
        const string json = """
        {
          "results": {
            "channels": [
              {
                "alternatives": [
                  {
                    "transcript": "Where is the meeting room?",
                    "confidence": 0.97
                  }
                ]
              }
            ]
          }
        }
        """;

        var result = DeepgramTranscriptParser.ParsePreRecordedResult(json);

        Assert.Equal("Where is the meeting room?", result.Text);
        Assert.True(result.IsFinal);
        Assert.Equal(SpeechRecognitionStatus.Completed, result.Status);
        Assert.Equal(0.97f, result.Confidence);
    }

    [Fact]
    public void ParseStreamingResult_returns_null_for_non_transcript_messages()
    {
        const string json = """
        {
          "type": "Metadata",
          "request_id": "test"
        }
        """;

        var result = DeepgramTranscriptParser.ParseStreamingResult(json);

        Assert.Null(result);
    }

    [Fact]
    public void ParseStreamingResult_maps_provider_error_events_to_a_safe_failure()
    {
        const string json = """
        {
          "type": "Error",
          "description": "provider details and request metadata"
        }
        """;

        var result = DeepgramTranscriptParser.ParseStreamingResult(json);

        Assert.NotNull(result);
        Assert.Equal(SpeechTranscriptStatus.Failed, result.Status);
        Assert.Equal("provider_unavailable", result.Error?.Code);
        Assert.DoesNotContain("provider details", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseStreamingResult_returns_failure_for_malformed_transcript_shape()
    {
        const string json = """
        {
          "channel": {
            "alternatives": {}
          }
        }
        """;

        var result = DeepgramTranscriptParser.ParseStreamingResult(json);

        Assert.NotNull(result);
        Assert.Equal(SpeechTranscriptStatus.Failed, result.Status);
        Assert.Equal("invalid_provider_response", result.Error?.Code);
    }

    [Fact]
    public void ParseStreamingResult_returns_failure_when_transcript_has_an_invalid_type()
    {
        const string json = """
        {
          "channel": {
            "alternatives": [
              {
                "transcript": 42
              }
            ]
          }
        }
        """;

        var result = DeepgramTranscriptParser.ParseStreamingResult(json);

        Assert.NotNull(result);
        Assert.Equal(SpeechTranscriptStatus.Failed, result.Status);
        Assert.Equal("invalid_provider_response", result.Error?.Code);
    }

    [Fact]
    public void ParseStreamingResult_throws_for_invalid_json_so_the_session_can_map_it()
    {
        Assert.Throws<System.Text.Json.JsonException>(
            () => DeepgramTranscriptParser.ParseStreamingResult("{not-json"));
    }

}
