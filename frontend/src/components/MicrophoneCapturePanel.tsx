import { useEffect, useRef, useState } from "react";
import {
  getMicrophoneErrorMessage,
  isMicrophoneCaptureSupported,
  startMicrophoneCapture,
  type MicrophoneCaptureSession
} from "../services/audio/microphoneCapture";

export function MicrophoneCapturePanel() {
  const [status, setStatus] = useState<
    "idle" | "starting" | "capturing" | "error"
  >("idle");
  const [error, setError] = useState<string | null>(null);
  const [chunkCount, setChunkCount] = useState(0);
  const [bytesCaptured, setBytesCaptured] = useState(0);
  const sessionRef = useRef<MicrophoneCaptureSession | null>(null);

  useEffect(() => {
    return () => {
      void sessionRef.current?.stop();
    };
  }, []);

  async function handleStart() {
    if (!isMicrophoneCaptureSupported()) {
      setError(getMicrophoneErrorMessage("unsupported"));
      setStatus("error");
      return;
    }

    setStatus("starting");
    setError(null);
    setChunkCount(0);
    setBytesCaptured(0);

    try {
      const session = await startMicrophoneCapture((chunk) => {
        setChunkCount((count) => count + 1);
        setBytesCaptured((bytes) => bytes + chunk.byteLength);
      });

      sessionRef.current = session;
      setStatus("capturing");
    } catch (captureError: unknown) {
      setError(getMicrophoneErrorMessage(captureError));
      setStatus("error");
    }
  }

  async function handleStop() {
    const session = sessionRef.current;
    sessionRef.current = null;

    if (session) {
      await session.stop();
    }

    setStatus("idle");
  }

  return (
    <section className="microphone-panel" aria-labelledby="microphone-panel-title">
      <div className="microphone-panel-header">
        <div>
          <span className="workspace-kicker">MICROPHONE</span>
          <h3 id="microphone-panel-title">Live audio capture</h3>
        </div>
        <span
          className={"microphone-status microphone-status-" + status}
          role="status"
        >
          {status === "starting"
            ? "Starting…"
            : status === "capturing"
              ? "Capturing"
              : status === "error"
                ? "Unavailable"
                : "Ready"}
        </span>
      </div>

      <p className="microphone-description">
        Allow microphone access to capture mono PCM audio locally in the browser.
        Nothing is sent to the server by this step.
      </p>

      <div className="microphone-format">
        <span>Format</span>
        <strong>Linear16 · 16 kHz · Mono</strong>
      </div>

      <div className="microphone-actions">
        {status === "capturing" ? (
          <button
            className="ghost-button"
            type="button"
            onClick={() => void handleStop()}
          >
            Stop capture
          </button>
        ) : (
          <button
            className="primary-button"
            type="button"
            onClick={() => void handleStart()}
            disabled={status === "starting"}
          >
            {status === "starting" ? "Requesting access…" : "Enable microphone"}
          </button>
        )}

        {status === "capturing" && (
          <span className="microphone-metrics" aria-live="polite">
            {chunkCount} chunks · {bytesCaptured.toLocaleString()} bytes
          </span>
        )}
      </div>

      {error && (
        <p className="microphone-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
