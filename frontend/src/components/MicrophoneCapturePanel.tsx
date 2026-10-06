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
  type AudioWebSocketStatus,
  type TranscriptUpdate
} from "../services/audio/audioWebSocket";
import {
  applyTranscriptUpdate,
  EMPTY_TRANSCRIPT,
  type TranscriptSnapshot
} from "../services/audio/transcriptState";
import type { LiveConnectionState } from "../services/audio/conversationTimelineState";
import { ConversationStatus } from "./ConversationStatus";

interface MicrophoneCapturePanelProps {
  conversationId: string | null;
  onFinalTranscript?: (text: string) => void;
  onTranscriptChange?: (transcript: TranscriptSnapshot) => void;
  onConnectionStateChange?: (state: LiveConnectionState) => void;
}

type PanelStatus = "idle" | "connecting" | "reconnecting" | "capturing" | "error";

const MAX_RECONNECT_ATTEMPTS = 3;
const RECONNECT_DELAY_MS = 1200;

export function MicrophoneCapturePanel({
  conversationId,
  onFinalTranscript,
  onTranscriptChange,
  onConnectionStateChange
}: MicrophoneCapturePanelProps) {
  const [status, setStatus] = useState<PanelStatus>("idle");
  const [socketStatus, setSocketStatus] = useState<AudioWebSocketStatus>("closed");
  const [error, setError] = useState<string | null>(null);
  const [chunkCount, setChunkCount] = useState(0);
  const [bytesCaptured, setBytesCaptured] = useState(0);
  const [transcript, setTranscript] = useState<TranscriptSnapshot>(EMPTY_TRANSCRIPT);
  const [reconnectAttempt, setReconnectAttempt] = useState(0);
  const sessionRef = useRef<MicrophoneCaptureSession | null>(null);
  const socketRef = useRef<AudioWebSocketClient | null>(null);
  const captureStartingRef = useRef(false);
  const manualStopRef = useRef(false);
  const reconnectTimerRef = useRef<number | null>(null);
  const reconnectAttemptRef = useRef(0);

  useEffect(() => {
    manualStopRef.current = true;
    void stopStreaming();
    return () => {
      manualStopRef.current = true;
      void stopStreaming();
    };
  }, [conversationId]);

  function clearReconnectTimer() {
    if (reconnectTimerRef.current !== null) {
      window.clearTimeout(reconnectTimerRef.current);
      reconnectTimerRef.current = null;
    }
  }

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
    reconnectAttemptRef.current = 0;
    setReconnectAttempt(0);
    captureStartingRef.current = false;
    setStatus("connecting");
    setSocketStatus("connecting");
    onConnectionStateChange?.("connecting");
    setError(null);
    setChunkCount(0);
    setBytesCaptured(0);
    setTranscript(EMPTY_TRANSCRIPT);
    onTranscriptChange?.(EMPTY_TRANSCRIPT);
    clearReconnectTimer();

    await connectSocket(conversationId);
  }

  async function connectSocket(targetConversationId: string) {
    if (manualStopRef.current) return;

    try {
      const socket = openAudioWebSocket(targetConversationId, {
        onTranscript: (update: TranscriptUpdate) => {
          setTranscript((current) => {
            const next = applyTranscriptUpdate(current, update);
            onTranscriptChange?.(next);
            if (
              update.type === "final_transcript" &&
              next.finalTexts.length > current.finalTexts.length
            ) {
              onFinalTranscript?.(next.finalTexts.at(-1) ?? "");
            }
            return next;
          });
        },
        onStatus: (nextStatus) => {
          setSocketStatus(nextStatus);

          if (nextStatus === "open") {
            onConnectionStateChange?.("connected");
            reconnectAttemptRef.current = 0;
            setReconnectAttempt(0);
            setError(null);
            void beginCapture(socket);
            return;
          }

          if (nextStatus === "error" && !manualStopRef.current) {
            onConnectionStateChange?.("error");
            setError("The audio connection was interrupted.");
          }

          if (nextStatus === "closed" && !manualStopRef.current) {
            onConnectionStateChange?.("disconnected");
            void cleanupCapture();
            scheduleReconnect(targetConversationId);
          }
        },
        onError: (nextError) => {
          if (!manualStopRef.current) {
            setError(nextError.message);
          }
        }
      });

      socketRef.current = socket;
    } catch (streamError: unknown) {
      await cleanupCapture();
      if (!manualStopRef.current) {
        setError(
          streamError instanceof Error
            ? streamError.message
            : "Microphone streaming could not be started."
        );
        setStatus("error");
        setSocketStatus("closed");
      }
    }
  }

  function scheduleReconnect(targetConversationId: string) {
    if (reconnectAttemptRef.current >= MAX_RECONNECT_ATTEMPTS) {
      setStatus("error");
      onConnectionStateChange?.("error");
      setError("The audio connection could not be restored. Start the microphone again.");
      return;
    }

    reconnectAttemptRef.current += 1;
    setReconnectAttempt(reconnectAttemptRef.current);
    setStatus("reconnecting");
    onConnectionStateChange?.("reconnecting");
    clearReconnectTimer();

    reconnectTimerRef.current = window.setTimeout(() => {
      reconnectTimerRef.current = null;
      void connectSocket(targetConversationId);
    }, RECONNECT_DELAY_MS);
  }

  async function beginCapture(socket: AudioWebSocketClient) {
    if (captureStartingRef.current || sessionRef.current) return;

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
      onConnectionStateChange?.("error");
      setError(getMicrophoneErrorMessage(captureError));
      setStatus("error");
    } finally {
      captureStartingRef.current = false;
    }
  }

  async function cleanupCapture() {
    const session = sessionRef.current;
    sessionRef.current = null;
    if (session) await session.stop().catch(() => undefined);
  }

  async function stopStreaming() {
    clearReconnectTimer();
    reconnectAttemptRef.current = 0;
    setReconnectAttempt(0);
    await cleanupCapture();

    const socket = socketRef.current;
    socketRef.current = null;
    if (socket) socket.close();

    setStatus("idle");
    setSocketStatus("closed");
    onConnectionStateChange?.("idle");
  }

  const startDisabled =
    !conversationId ||
    status === "connecting" ||
    status === "reconnecting" ||
    status === "capturing";

  return (
    <section className="microphone-panel" aria-labelledby="microphone-panel-title">
      <div className="microphone-panel-header">
        <div>
          <span className="workspace-kicker">MICROPHONE</span>
          <h3 id="microphone-panel-title">Live audio streaming</h3>
        </div>
        <span className={"microphone-status microphone-status-" + status} role="status">
          {status === "connecting"
            ? "Connecting…"
            : status === "reconnecting"
              ? "Reconnecting…"
              : status === "capturing"
                ? "Streaming"
                : status === "error"
                  ? "Disconnected"
                  : "Ready"}
        </span>
      </div>

      {status === "reconnecting" && (
        <ConversationStatus
          tone="warning"
          title="Connection interrupted"
          detail={"Trying to reconnect (" + reconnectAttempt + "/" + MAX_RECONNECT_ATTEMPTS + ")…"}
        />
      )}

      {status === "error" && error && (
        <ConversationStatus
          tone="error"
          title="Microphone unavailable"
          detail={error}
          actionLabel="Try again"
          onAction={() => void handleStart()}
        />
      )}

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
            {status === "connecting" || status === "reconnecting"
              ? "Connecting…"
              : "Start microphone"}
          </button>
        )}

        <span className="microphone-metrics" aria-live="polite">
          {socketStatus === "open"
            ? "WebSocket connected"
            : status === "reconnecting"
              ? "WebSocket reconnecting"
              : "WebSocket disconnected"}
          {status === "capturing" &&
            " · " + chunkCount + " chunks · " + bytesCaptured.toLocaleString() + " bytes"}
        </span>
      </div>

      {error && status !== "error" && (
        <p className="microphone-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
