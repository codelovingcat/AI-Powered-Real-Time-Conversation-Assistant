import { useEffect, useRef, useState } from "react";
import {
  getMicrophoneErrorMessage,
  isMicrophoneCaptureSupported,
  startMicrophoneCapture,
  type MicrophoneCaptureSession
} from "../services/audio/microphoneCapture";
import {
  openAudioWebSocket,
  type AudioWebSocketClient,
  type AudioWebSocketStatus
} from "../services/audio/audioWebSocket";

interface MicrophoneCapturePanelProps {
  conversationId: string | null;
}

type PanelStatus = "idle" | "connecting" | "capturing" | "error";

export function MicrophoneCapturePanel({
  conversationId
}: MicrophoneCapturePanelProps) {
  const [status, setStatus] = useState<PanelStatus>("idle");
  const [socketStatus, setSocketStatus] =
    useState<AudioWebSocketStatus>("closed");
  const [error, setError] = useState<string | null>(null);
  const [chunkCount, setChunkCount] = useState(0);
  const [bytesCaptured, setBytesCaptured] = useState(0);
  const sessionRef = useRef<MicrophoneCaptureSession | null>(null);
  const socketRef = useRef<AudioWebSocketClient | null>(null);
  const captureStartingRef = useRef(false);
  const manualStopRef = useRef(false);

  useEffect(() => {
    manualStopRef.current = true;
    void stopStreaming();

    return () => {
      manualStopRef.current = true;
      void stopStreaming();
    };
  }, [conversationId]);

  async function handleStart() {
    if (!conversationId) {
      setError("Select a conversation before starting microphone streaming.");
      setStatus("error");
      return;
    }

    if (!isMicrophoneCaptureSupported()) {
      setError(getMicrophoneErrorMessage("unsupported"));
      setStatus("error");
      return;
    }

    manualStopRef.current = false;
    captureStartingRef.current = false;
    setStatus("connecting");
    setSocketStatus("connecting");
    setError(null);
    setChunkCount(0);
    setBytesCaptured(0);

    try {
      const socket = openAudioWebSocket(conversationId, {
        onStatus: (nextStatus) => {
          setSocketStatus(nextStatus);

          if (nextStatus === "open") {
            void beginCapture(socket);
            return;
          }

          if (
            (nextStatus === "error" || nextStatus === "closed") &&
            !manualStopRef.current
          ) {
            void cleanupCapture();
            setError(
              "The audio WebSocket connection was closed before streaming could continue."
            );
            setStatus("error");
          }
        },
        onError: (nextError) => {
          if (!manualStopRef.current) {
            setError(nextError.message);
            setStatus("error");
          }
        }
      });

      socketRef.current = socket;
    } catch (streamError: unknown) {
      await cleanupCapture();
      setError(
        streamError instanceof Error
          ? streamError.message
          : "Microphone streaming could not be started."
      );
      setStatus("error");
      setSocketStatus("closed");
    }
  }

  async function beginCapture(socket: AudioWebSocketClient) {
    if (captureStartingRef.current || sessionRef.current) {
      return;
    }

    captureStartingRef.current = true;

    try {
      const session = await startMicrophoneCapture((chunk) => {
        socket.sendAudioChunk(chunk);
        setChunkCount((count) => count + 1);
        setBytesCaptured((bytes) => bytes + chunk.byteLength);
      });

      if (manualStopRef.current || socketRef.current !== socket) {
        await session.stop();
        return;
      }

      sessionRef.current = session;
      setStatus("capturing");
    } catch (captureError: unknown) {
      socket.close();
      setError(getMicrophoneErrorMessage(captureError));
      setStatus("error");
    } finally {
      captureStartingRef.current = false;
    }
  }

  async function cleanupCapture() {
    const session = sessionRef.current;
    sessionRef.current = null;

    if (session) {
      await session.stop().catch(() => undefined);
    }
  }

  async function stopStreaming() {
    await cleanupCapture();

    const socket = socketRef.current;
    socketRef.current = null;

    if (socket) {
      socket.close();
    }

    setStatus("idle");
    setSocketStatus("closed");
  }

  const startDisabled =
    !conversationId ||
    status === "connecting" ||
    status === "capturing";

  return (
    <section className="microphone-panel" aria-labelledby="microphone-panel-title">
      <div className="microphone-panel-header">
        <div>
          <span className="workspace-kicker">MICROPHONE</span>
          <h3 id="microphone-panel-title">Live audio streaming</h3>
        </div>
        <span
          className={"microphone-status microphone-status-" + status}
          role="status"
        >
          {status === "connecting"
            ? "Connecting…"
            : status === "capturing"
              ? "Streaming"
              : status === "error"
                ? "Unavailable"
                : "Ready"}
        </span>
      </div>

      <p className="microphone-description">
        {conversationId
          ? "Allow microphone access to stream mono PCM audio to the active conversation."
          : "Select a conversation to enable microphone streaming."}
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
            onClick={() => {
              manualStopRef.current = true;
              void stopStreaming();
            }}
          >
            Stop streaming
          </button>
        ) : (
          <button
            className="primary-button"
            type="button"
            onClick={() => void handleStart()}
            disabled={startDisabled}
          >
            {status === "connecting" ? "Connecting…" : "Start microphone"}
          </button>
        )}

        <span className="microphone-metrics" aria-live="polite">
          {socketStatus === "open" ? "WebSocket connected" : "WebSocket closed"}
          {status === "capturing" &&
            ` · ${chunkCount} chunks · ${bytesCaptured.toLocaleString()} bytes`}
        </span>
      </div>

      {error && (
        <p className="microphone-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
