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
}
