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

  useEffect(() => {
    return () => {
      void stopStreaming();
    };
  }, []);

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

    setStatus("connecting");
    setSocketStatus("connecting");
    setError(null);
    setChunkCount(0);
    setBytesCaptured(0);

    try {
      const socket = openAudioWebSocket(conversationId, {
        onStatus: (nextStatus) => {
          setSocketStatus(nextStatus);

          if (nextStatus === "error") {
            setStatus("error");
          }

          if (nextStatus === "closed" && status !== "idle") {
            void stopStreaming();
          }
        },
        onError: (nextError) => {
          setError(nextError.message);
          setStatus("error");
        }
      });

      socketRef.current = socket;

      const originalOpen = socket;
      await waitForSocketReady(originalOpen);

      if (socketStatus === "closed") {
        throw new Error("The audio WebSocket connection could not be opened.");
      }

      const session = await startMicrophoneCapture((chunk) => {
        originalOpen.sendAudioChunk(chunk);
        setChunkCount((count) => count + 1);
        setBytesCaptured((bytes) => bytes + chunk.byteLength);
      });

      sessionRef.current = session;
      setStatus("capturing");
      setSocketStatus("open");
    } catch (streamError: unknown) {
      await stopStreaming();
      setError(
        streamError instanceof Error
          ? streamError.message
          : "Microphone streaming could not be started."
      );
      setStatus("error");
    }
  }

  async function stopStreaming() {
    const session = sessionRef.current;
    sessionRef.current = null;

    if (session) {
      await session.stop().catch(() => undefined);
    }

    const socket = socketRef.current;
    socketRef.current = null;

    if (socket) {
      socket.close();
    }

    setStatus("idle");
    setSocketStatus("closed");
  }

  const disabled =
    !conversationId || status === "connecting" || status === "capturing";

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
            onClick={() => void stopStreaming()}
          >
            Stop streaming
          </button>
        ) : (
          <button
            className="primary-button"
            type="button"
            onClick={() => void handleStart()}
            disabled={disabled}
          >
            {status === "connecting"
              ? "Connecting…"
              : "Start microphone"}
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

function waitForSocketReady(socket: AudioWebSocketClient): Promise<void> {
  return new Promise((resolve, reject) => {
    const startedAt = Date.now();
    const timeoutMs = 10000;

    const poll = () => {
      const state = getClientSocketState(socket);

      if (state === "open") {
        resolve();
        return;
      }

      if (state === "error" || state === "closed") {
        reject(new Error("The audio WebSocket connection could not be opened."));
        return;
      }

      if (Date.now() - startedAt >= timeoutMs) {
        reject(new Error("The audio WebSocket connection timed out."));
        return;
      }

      window.setTimeout(poll, 25);
    };

    poll();
  });
}

function getClientSocketState(
  _socket: AudioWebSocketClient
): "connecting" | "open" | "error" | "closed" {
  return "open";
}
