import type { TranscriptSnapshot } from "../services/audio/transcriptState";

interface LiveTranscriptProps {
  transcript: TranscriptSnapshot;
}

export function LiveTranscript({ transcript }: LiveTranscriptProps) {
  const hasContent =
    transcript.finalTexts.length > 0 || transcript.partialText.length > 0;

  return (
    <section className="live-transcript" aria-labelledby="live-transcript-title">
      <div className="live-transcript-header">
        <div>
          <span className="workspace-kicker">LIVE TRANSCRIPT</span>
          <h3 id="live-transcript-title">What we hear</h3>
        </div>
        {transcript.confidence !== null && (
          <span className="transcript-confidence">
            {Math.round(transcript.confidence * 100)}% confidence
          </span>
        )}
      </div>

      {!hasContent ? (
        <p className="live-transcript-empty" aria-live="polite">
          Start the microphone to see recognized speech here.
        </p>
      ) : (
        <div className="transcript-stream" aria-live="polite">
          {transcript.finalTexts.map((text, index) => (
            <p className="transcript-final" key={index}>
              {text}
            </p>
          ))}
          {transcript.partialText && (
            <p className="transcript-partial" aria-label="Partial transcript">
              {transcript.partialText}
            </p>
          )}
        </div>
      )}
    </section>
  );
}
