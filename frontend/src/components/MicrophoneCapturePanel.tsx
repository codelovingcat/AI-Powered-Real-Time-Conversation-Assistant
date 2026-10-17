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
import {
  transitionConversationLifecycle,
  type ConversationLifecycleAction,
  type ConversationLifecycleState
} from "../services/api/conversationService";
import { getConversationErrorMessage } from "../services/api/conversationService";
import { ConversationStatus } from "./ConversationStatus";

interface MicrophoneCapturePanelProps {
  conversationId: string | null;
  lifecycleState: ConversationLifecycleState;
  onLifecycleStateChange: (state: ConversationLifecycleState) => void;
  onFinalTranscript?: (text: string) => void;
  onTranscriptChange?: (transcript: TranscriptSnapshot) => void;
  onConnectionStateChange?: (state: LiveConnectionState) => void;
}

type PanelStatus =
  | "idle"
  | "connecting"
  | "reconnecting"
  | "capturing"
  | "paused"
  | "finishing"
  | "error";

type RetryAction = "capture" | "finish" | null;

const MAX_RECONNECT_ATTEMPTS = 3;
const RECONNECT_DELAY_MS = 1200;

export function MicrophoneCapturePanel({
  conversationId,
  lifecycleState,
  onLifecycleStateChange,
  onFinalTranscript,
  onTranscriptChange,
  onConnectionStateChange
}: MicrophoneCapturePanelProps) {
  const [status, setStatus] = useState<PanelStatus>("idle");
  const [socketStatus, setSocketStatus] = useState<AudioWebSocketStatus>("closed");
  const [error, setError] = useState<string | null>(null);
  const [retryAction, setRetryAction] = useState<RetryAction>(null);
  const [isLifecycleBusy, setIsLifecycleBusy] = useState(false);
  const [chunkCount, setChunkCount] = useState(0);
  const [bytesCaptured, setBytesCaptured] = useState(0);
  const [transcript, setTranscript] = useState<TranscriptSnapshot>(EMPTY_TRANSCRIPT);
  const [reconnectAttempt, setReconnectAttempt] = useState(0);
  const sessionRef = useRef<MicrophoneCaptureSession | null>(null);
  const socketRef = useRef<AudioWebSocketClient | null>(null);
  const captureStartingRef = useRef(false);
  const lifecycleActionInFlightRef = useRef(false);
  const manualStopRef = useRef(false);
  const pausedRef = useRef(false);
  const reconnectTimerRef = useRef<number | null>(null);
  const reconnectAttemptRef = useRef(0);
  const connectionGenerationRef = useRef(0);

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

  function lockLifecycleAction(): boolean {
    if (lifecycleActionInFlightRef.current) {
      return false;
    }

    lifecycleActionInFlightRef.current = true;
    setIsLifecycleBusy(true);
    return true;
  }

  function unlockLifecycleAction() {
    lifecycleActionInFlightRef.current = false;
    setIsLifecycleBusy(false);
  }

  async function handleStart() {
    if (
      !conversationId ||
      lifecycleState === "completed" ||
      lifecycleActionInFlightRef.current ||
      captureStartingRef.current ||
      status === "connecting" ||
      status === "reconnecting" ||
      status === "capturing"
    ) {
      return;
    }

    if (!isMicrophoneCaptureSupported()) {
      setError(getMicrophoneErrorMessage("unsupported"));
      setRetryAction("capture");
      setStatus("error");
      return;
    }

    if (!lockLifecycleAction()) {
      return;
    }

    const action: ConversationLifecycleAction =
      lifecycleState === "paused" ? "resume" : "start";

    manualStopRef.current = false;
    pausedRef.current = false;
    reconnectAttemptRef.current = 0;
    setReconnectAttempt(0);
    setStatus("connecting");
    setSocketStatus("connecting");
    onConnectionStateChange?.("connecting");
    setError(null);
    setRetryAction(null);
    clearReconnectTimer();

    if (lifecycleState === "ready") {
      setChunkCount(0);
      setBytesCaptured(0);
      setTranscript(EMPTY_TRANSCRIPT);
      onTranscriptChange?.(EMPTY_TRANSCRIPT);
    }

    try {
      const updated = await transitionConversationLifecycle(conversationId, action);
      onLifecycleStateChange(updated.lifecycleState ?? "active");
      await connectSocket(conversationId);
    } catch (requestError: unknown) {
      manualStopRef.current = true;
      await cleanupCapture();
      const socket = socketRef.current;
      socketRef.current = null;
      if (socket) socket.close();
      setError(getConversationErrorMessage(requestError));
      setRetryAction(action === "resume" ? "capture" : "capture");
      setStatus("error");
      setSocketStatus("closed");
      onConnectionStateChange?.("error");
    } finally {
      unlockLifecycleAction();
    }
  }

  async function connectSocket(targetConversationId: string) {
    if (manualStopRef.current || pausedRef.current) return;

    const generation = ++connectionGenerationRef.current;
    try {
      const socket = openAudioWebSocket(targetConversationId, {
        onTranscript: (update: TranscriptUpdate) => {
          if (
            generation !== connectionGenerationRef.current ||
            manualStopRef.current ||
            pausedRef.current
          ) {
            return;
          }

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
          if (generation !== connectionGenerationRef.current) {
            return;
          }

          setSocketStatus(nextStatus);

          if (nextStatus === "open") {
            if (manualStopRef.current || pausedRef.current) {
              socket.close();
              return;
            }

            onConnectionStateChange?.("connected");
            reconnectAttemptRef.current = 0;
            setReconnectAttempt(0);
            setError(null);
            setRetryAction(null);
            void beginCapture(socket);
            return;
          }

          if (nextStatus === "error" && !manualStopRef.current) {
            onConnectionStateChange?.("error");
            setError("The audio connection was interrupted.");
            setRetryAction("capture");
          }

          if (nextStatus === "closed" && !manualStopRef.current) {
            onConnectionStateChange?.("disconnected");
            if (pausedRef.current) {
              void cleanupCapture();
              setStatus("paused");
              return;
            }
            void cleanupCapture();
            scheduleReconnect(targetConversationId);
          }
        },
        onError: () => {
          if (generation === connectionGenerationRef.current && !manualStopRef.current) {
            setError("The audio connection was interrupted.");
            setRetryAction("capture");
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
        setRetryAction("capture");
        setStatus("error");
        setSocketStatus("closed");
      }
    }
  }

  function scheduleReconnect(targetConversationId: string) {
    if (pausedRef.current) {
      setStatus("paused");
      return;
    }

    if (reconnectAttemptRef.current >= MAX_RECONNECT_ATTEMPTS) {
      setStatus("error");
      setRetryAction("capture");
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
    if (captureStartingRef.current || sessionRef.current || pausedRef.current) return;

    captureStartingRef.current = true;

    try {
      const session = await startMicrophoneCapture((chunk) => {
        if (pausedRef.current || manualStopRef.current) {
          return;
        }
        socket.sendAudioChunk(chunk);
        setChunkCount((count) => count + 1);
        setBytesCaptured((bytes) => bytes + chunk.byteLength);
      });

      if (
        manualStopRef.current ||
        pausedRef.current ||
        socketRef.current !== socket
      ) {
        await session.stop();
        return;
      }

      sessionRef.current = session;
      setStatus("capturing");
    } catch (captureError: unknown) {
      socket.close();
      onConnectionStateChange?.("error");
      setError(getMicrophoneErrorMessage(captureError));
      setRetryAction("capture");
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
    connectionGenerationRef.current += 1;
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

  async function handlePause() {
    if (
      !conversationId ||
      lifecycleState !== "active" ||
      status !== "capturing" ||
      !lockLifecycleAction()
    ) {
      return;
    }

    setError(null);
    setRetryAction(null);

    try {
      const updated = await transitionConversationLifecycle(conversationId, "pause");
      onLifecycleStateChange(updated.lifecycleState ?? "paused");

      pausedRef.current = true;
      manualStopRef.current = true;
      connectionGenerationRef.current += 1;
      clearReconnectTimer();
      await cleanupCapture();

      const socket = socketRef.current;
      socketRef.current = null;
      if (socket) socket.close();

      setSocketStatus("closed");
      setStatus("paused");
      onConnectionStateChange?.("idle");
    } catch (requestError: unknown) {
      setError(getConversationErrorMessage(requestError));
      setRetryAction(null);
    } finally {
      unlockLifecycleAction();
    }
  }

  async function handleFinish() {
    if (
      !conversationId ||
      lifecycleState === "completed" ||
      !lockLifecycleAction()
    ) {
      return;
    }

    manualStopRef.current = true;
    pausedRef.current = false;
    connectionGenerationRef.current += 1;
    clearReconnectTimer();
    setStatus("finishing");
    setError(null);
    setRetryAction(null);

    // Stop local capture immediately, even if the API is temporarily unavailable.
    await cleanupCapture();
    const socket = socketRef.current;
    socketRef.current = null;
    if (socket) socket.close();
    setSocketStatus("closed");
    onConnectionStateChange?.("idle");

    try {
      const updated = await transitionConversationLifecycle(conversationId, "finish");
      onLifecycleStateChange(updated.lifecycleState ?? "completed");
      setStatus("idle");
      setError(null);
    } catch {
      setStatus("error");
      setRetryAction("finish");
      setError(
        "The microphone was stopped, but the conversation could not be marked as finished. Check your connection and try again."
      );
    } finally {
      unlockLifecycleAction();
    }
  }

  const lifecycleIsCompleted = lifecycleState === "completed";
  const noConversationSelected = !conversationId;
  const startDisabled =
    !conversationId ||
    lifecycleIsCompleted ||
    isLifecycleBusy ||
    status === "connecting" ||
    status === "reconnecting" ||
    status === "capturing" ||
    status === "finishing";
  const statusLabel = noConversationSelected
    ? "Select a conversation"
    : lifecycleIsCompleted
      ? "Finished"
    : status === "connecting"
      ? "Connecting…"
      : status === "reconnecting"
        ? "Reconnecting…"
        : status === "capturing"
          ? "Streaming"
          : status === "paused" || lifecycleState === "paused"
            ? "Paused"
            : status === "finishing"
              ? "Finishing…"
              : status === "error"
                ? "Interrupted"
                : lifecycleState === "ready"
                  ? "Ready to start"
                  : "Not recording";
  const startLabel =
    status === "connecting" || status === "reconnecting"
      ? "Connecting…"
      : lifecycleState === "paused"
        ? "Resume conversation"
        : lifecycleState === "ready"
          ? "Start conversation & microphone"
          : "Start microphone";

  return (
    <section className="microphone-panel" aria-labelledby="microphone-panel-title">
      <div className="microphone-panel-header">
        <div>
          <span className="workspace-kicker">CONVERSATION LIFECYCLE</span>
          <h3 id="microphone-panel-title">Live conversation</h3>
        </div>
        <span className={"microphone-status microphone-status-" + status} role="status">
          {statusLabel}
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
          title={retryAction === "finish" ? "Conversation finish not saved" : "Microphone or connection interrupted"}
          detail={error}
          actionLabel={retryAction === "finish" ? "Retry finish" : "Try again"}
          onAction={() => {
            if (retryAction === "finish") {
              void handleFinish();
            } else {
              void handleStart();
            }
          }}
        />
      )}

      <p className="microphone-description">
        {noConversationSelected
          ? "Select a conversation to start, pause, resume, or finish its live session."
          : lifecycleIsCompleted
            ? "This conversation is finished. Its messages remain available in history."
          : lifecycleState === "paused"
            ? "Recording is paused and the microphone has been released. Resume when you're ready."
            : lifecycleState === "ready"
              ? "Start the conversation to open a secure audio stream and request microphone access."
              : status === "capturing"
                ? "Audio is being streamed to the active conversation. Pause to stop sending audio, or finish to close the session."
                : "Your conversation is active. Start or retry the microphone stream to continue recording."}
      </p>

      <div className="microphone-format">
        <span>Format</span>
        <strong>Linear16 · 16 kHz · Mono</strong>
      </div>

      <div className="microphone-actions">
        {!noConversationSelected && !lifecycleIsCompleted && (
          <>
            {status === "capturing" ? (
              <button
                className="ghost-button"
                type="button"
                onClick={() => void handlePause()}
                disabled={isLifecycleBusy}
              >
                {isLifecycleBusy ? "Pausing…" : "Pause conversation"}
              </button>
            ) : (
              <button
                className="primary-button"
                type="button"
                onClick={() => void handleStart()}
                disabled={startDisabled}
                aria-label={lifecycleState === "paused" ? "Resume conversation" : "Start microphone"}
                aria-busy={status === "connecting" || status === "reconnecting"}
              >
                {startLabel}
              </button>
            )}

            <button
              className="ghost-button finish-conversation-button"
              type="button"
              onClick={() => void handleFinish()}
              disabled={isLifecycleBusy || status === "finishing"}
            >
              {status === "finishing" ? "Finishing…" : "Finish conversation"}
            </button>
          </>
        )}

        <span className="microphone-metrics" aria-live="polite">
          {socketStatus === "open" && status === "capturing"
            ? "WebSocket connected"
            : status === "reconnecting"
              ? "WebSocket reconnecting"
              : status === "paused"
                ? "Microphone released · Conversation paused"
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
