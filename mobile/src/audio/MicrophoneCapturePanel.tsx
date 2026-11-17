import { useEffect, useMemo, useRef, useState } from "react";
import {
  AppState,
  Linking,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  View
} from "react-native";
import { AudioManager, AudioRecorder } from "react-native-audio-api";
import {
  transitionConversationLifecycle,
  type ConversationLifecycleState
} from "../api/conversationService";
import {
  openAudioWebSocket,
  type AudioWebSocketClient,
  type AudioWebSocketStatus,
  type TranscriptUpdate
} from "../api/audioWebSocket";
import { float32ToPcm16 } from "./microphoneCapture";

interface MicrophoneCapturePanelProps {
  conversationId: string | null;
  lifecycleState: ConversationLifecycleState;
  onLifecycleStateChange: (state: ConversationLifecycleState) => void;
  onFinalTranscript: (text: string) => void;
}

type PanelState =
  | "idle"
  | "requestingPermission"
  | "connecting"
  | "recording"
  | "reconnecting"
  | "paused"
  | "finishing"
  | "error";

const MAX_RECONNECT_ATTEMPTS = 3;
const RECONNECT_DELAY_MS = 1200;

export function MicrophoneCapturePanel({
  conversationId,
  lifecycleState,
  onLifecycleStateChange,
  onFinalTranscript
}: MicrophoneCapturePanelProps) {
  const recorder = useMemo(() => {
    const instance = new AudioRecorder();
    // Live chunks are streamed directly. Never create a local recording file.
    instance.disableFileOutput();
    return instance;
  }, []);

  const [state, setState] = useState<PanelState>("idle");
  const [socketStatus, setSocketStatus] = useState<AudioWebSocketStatus>("closed");
  const [permissionMessage, setPermissionMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [partialText, setPartialText] = useState("");
  const [finalTexts, setFinalTexts] = useState<string[]>([]);
  const [chunkCount, setChunkCount] = useState(0);
  const [reconnectAttempt, setReconnectAttempt] = useState(0);
  const socketRef = useRef<AudioWebSocketClient | null>(null);
  const captureActiveRef = useRef(false);
  const manualStopRef = useRef(true);
  const serverFailureRef = useRef(false);
  const reconnectAttemptRef = useRef(0);
  const reconnectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const generationRef = useRef(0);
  const startingRef = useRef(false);
  const finalTranscriptHandlerRef = useRef(onFinalTranscript);
  const lastFinalTranscriptRef = useRef("");

  finalTranscriptHandlerRef.current = onFinalTranscript;

  useEffect(() => {
    recorder.onAudioReady(
      {
        sampleRate: 16000,
        bufferLength: 1600,
        channelCount: 1
      },
      ({ buffer, numFrames }) => {
        if (!captureActiveRef.current) {
          return;
        }

        const samples = buffer.getChannelData(0).subarray(0, numFrames);
        const pcm = float32ToPcm16(samples, buffer.sampleRate);
        if (pcm.byteLength === 0) {
          return;
        }

        const sent = socketRef.current?.sendAudioChunk(pcm) ?? false;
        if (sent) {
          setChunkCount((current) => current + 1);
        }
      }
    );

    recorder.onError(() => {
      void stopLocalCapture();
      setError("Microphone capture stopped unexpectedly. Check device permissions and try again.");
      setState("error");
    });

    return () => {
      manualStopRef.current = true;
      generationRef.current += 1;
      clearReconnectTimer();
      socketRef.current?.close();
      socketRef.current = null;
      void stopLocalCapture();
      recorder.clearOnAudioReady();
      recorder.clearOnError();
    };
    // A single native recorder instance is intentionally retained for this panel.
  }, [recorder]);

  useEffect(() => {
    const subscription = AppState.addEventListener("change", (nextState) => {
      if (nextState === "active") {
        return;
      }

      manualStopRef.current = true;
      generationRef.current += 1;
      clearReconnectTimer();
      socketRef.current?.close();
      socketRef.current = null;
      void stopLocalCapture();
      setSocketStatus("closed");
      setState("paused");
      setPermissionMessage(
        "Streaming stopped because Conversa moved to the background. Start again when you return."
      );
    });

    return () => subscription.remove();
  }, []);

  useEffect(() => {
    // Switching to another conversation always tears down the old microphone/socket.
    manualStopRef.current = true;
    generationRef.current += 1;
    clearReconnectTimer();
    socketRef.current?.close();
    socketRef.current = null;
    void stopLocalCapture();
    setPartialText("");
    setFinalTexts([]);
    lastFinalTranscriptRef.current = "";
    setChunkCount(0);
    setReconnectAttempt(0);
    setSocketStatus("closed");
    setState("idle");
    setError(null);
    setPermissionMessage(null);

    return () => {
      manualStopRef.current = true;
      generationRef.current += 1;
      clearReconnectTimer();
      socketRef.current?.close();
      socketRef.current = null;
      void stopLocalCapture();
    };
  }, [conversationId]);

  function clearReconnectTimer() {
    if (reconnectTimerRef.current) {
      clearTimeout(reconnectTimerRef.current);
      reconnectTimerRef.current = null;
    }
  }

  async function stopLocalCapture() {
    captureActiveRef.current = false;

    try {
      if (recorder.isRecording()) {
        await recorder.stop();
      }
    } catch {
      // The native recorder may already have stopped after permission revocation.
    }

    if (Platform.OS === "ios") {
      try {
        await AudioManager.setAudioSessionActivity(false);
      } catch {
        // Audio session cleanup is best-effort after the microphone has been stopped.
      }
    }
  }

  async function startLocalCapture(): Promise<void> {
    const permission = await AudioManager.requestRecordingPermissions();
    if (permission !== "Granted") {
      throw new Error(
        "Microphone access is required. Allow it in device settings and try again."
      );
    }

    if (Platform.OS === "ios") {
      AudioManager.setAudioSessionOptions({
        iosCategory: "record",
        iosMode: "default",
        iosOptions: []
      });
      await AudioManager.setAudioSessionActivity(true);
    }

    const result = await recorder.start();
    if (result.status === "error") {
      if (Platform.OS === "ios") {
        await AudioManager.setAudioSessionActivity(false);
      }
      throw new Error("The microphone could not be started. Check device permissions and try again.");
    }

    captureActiveRef.current = true;
  }

  function handleTranscript(update: TranscriptUpdate) {
    const text = update.text.trim();
    if (!text) {
      return;
    }

    if (update.type === "partial_transcript") {
      setPartialText(text);
      return;
    }

    setPartialText("");
    if (lastFinalTranscriptRef.current === text) {
      return;
    }
    lastFinalTranscriptRef.current = text;
    setFinalTexts((current) => {
      // The live screen keeps a bounded recent timeline; complete history is loaded from API.
      return [...current, text].slice(-30);
    });
    finalTranscriptHandlerRef.current(text);
  }

  async function connectAndCapture(isReconnect: boolean): Promise<void> {
    if (!conversationId || startingRef.current) {
      return;
    }

    startingRef.current = true;
    const generation = ++generationRef.current;
    manualStopRef.current = false;
    serverFailureRef.current = false;

    try {
      if (!isReconnect) {
        setState("requestingPermission");
        setPermissionMessage(null);
        setError(null);

        if (lifecycleState === "ready") {
          const updated = await transitionConversationLifecycle(conversationId, "start");
          onLifecycleStateChange(updated.lifecycleState);
        } else if (lifecycleState === "paused") {
          const updated = await transitionConversationLifecycle(conversationId, "resume");
          onLifecycleStateChange(updated.lifecycleState);
        }
      }

      setState(isReconnect ? "reconnecting" : "connecting");
      setSocketStatus("connecting");

      const client = await openAudioWebSocket(conversationId, {
        onStatus: (nextStatus) => {
          if (generation !== generationRef.current) {
            return;
          }

          setSocketStatus(nextStatus);

          if (nextStatus === "closed" && !manualStopRef.current) {
            void handleUnexpectedDisconnect(generation);
          }
        },
        onTranscript: handleTranscript,
        onServerError: (serverError) => {
          if (generation !== generationRef.current) {
            return;
          }

          serverFailureRef.current = true;
          manualStopRef.current = true;
          setError(getSpeechErrorMessage(serverError.code));
          setState("error");
          setPermissionMessage(null);
          socketRef.current?.close();
          void stopLocalCapture();
        },
        onError: () => {
          if (generation === generationRef.current && !manualStopRef.current) {
            setError("The audio connection was interrupted.");
          }
        }
      });

      if (generation !== generationRef.current || manualStopRef.current) {
        client.close();
        return;
      }

      socketRef.current = client;
      await startLocalCapture();

      if (generation !== generationRef.current || manualStopRef.current) {
        await stopLocalCapture();
        client.close();
        return;
      }

      reconnectAttemptRef.current = 0;
      setReconnectAttempt(0);
      setError(null);
      setPermissionMessage(null);
      setState("recording");
    } catch (reason: unknown) {
      if (generation === generationRef.current && !manualStopRef.current) {
        manualStopRef.current = true;
        socketRef.current?.close();
        socketRef.current = null;
        await stopLocalCapture();
        setState("error");
        const safeMessage = reason instanceof Error
          ? reason.message
          : "The live conversation could not be started.";
        setError(safeMessage);
        if (safeMessage.toLowerCase().includes("microphone access")) {
          setPermissionMessage(safeMessage);
        }
      }
    } finally {
      startingRef.current = false;
    }
  }

  async function handleUnexpectedDisconnect(generation: number) {
    if (
      manualStopRef.current ||
      serverFailureRef.current ||
      generation !== generationRef.current
    ) {
      return;
    }

    await stopLocalCapture();
    socketRef.current = null;

    if (reconnectAttemptRef.current >= MAX_RECONNECT_ATTEMPTS) {
      setState("error");
      setError("The audio connection could not be restored. Start the microphone again.");
      return;
    }

    reconnectAttemptRef.current += 1;
    setReconnectAttempt(reconnectAttemptRef.current);
    setState("reconnecting");
    setError("Connection interrupted. Trying to reconnect…");
    clearReconnectTimer();

    reconnectTimerRef.current = setTimeout(() => {
      reconnectTimerRef.current = null;
      void connectAndCapture(true);
    }, RECONNECT_DELAY_MS);
  }

  async function stopStreaming(action: "pause" | "finish" | "stop") {
    manualStopRef.current = true;
    generationRef.current += 1;
    clearReconnectTimer();
    reconnectAttemptRef.current = 0;
    setReconnectAttempt(0);

    const client = socketRef.current;
    socketRef.current = null;
    client?.close();
    await stopLocalCapture();
    setSocketStatus("closed");
    setPartialText("");

    if ((action === "pause" || action === "finish") && conversationId) {
      try {
        const updated = await transitionConversationLifecycle(conversationId, action);
        onLifecycleStateChange(updated.lifecycleState);
        setState(action === "pause" ? "paused" : "idle");
        setError(null);
        setPermissionMessage(null);
      } catch {
        setState("error");
        setError(
          action === "finish"
            ? "The microphone stopped, but the conversation could not be marked as finished. Try again."
            : "The microphone stopped, but the pause could not be saved. Check your connection."
        );
      }
      return;
    }

    setState("idle");
  }

  const canStart =
    !!conversationId &&
    lifecycleState !== "completed" &&
    state !== "connecting" &&
    state !== "requestingPermission" &&
    state !== "recording" &&
    state !== "reconnecting" &&
    state !== "finishing";

  return (
    <View style={styles.panel}>
      <View style={styles.header}>
        <View style={styles.headingBlock}>
          <Text style={styles.eyebrow}>LIVE CONVERSATION</Text>
          <Text style={styles.heading}>Speak naturally</Text>
        </View>
        <Text
          accessibilityLiveRegion="polite"
          style={[
            styles.status,
            state === "recording" ? styles.statusActive : null,
            state === "error" ? styles.statusError : null
          ]}
        >
          {state === "recording"
            ? "Streaming"
            : state === "connecting" || state === "requestingPermission"
              ? "Connecting"
              : state === "reconnecting"
                ? "Reconnecting"
                : state === "paused"
                  ? "Paused"
                  : state === "error"
                    ? "Interrupted"
                    : lifecycleState === "completed"
                      ? "Finished"
                      : "Ready"}
        </Text>
      </View>

      <Text style={styles.description}>
        English speech is transcribed live, then sent to Conversa for Turkish translation and conversation assistance.
      </Text>

      <View style={styles.formatRow}>
        <Text style={styles.formatLabel}>Audio format</Text>
        <Text style={styles.formatValue}>Linear16 · 16 kHz · Mono</Text>
      </View>

      {permissionMessage ? (
        <Text style={styles.notice} accessibilityLiveRegion="polite">
          {permissionMessage}
        </Text>
      ) : null}

      {error ? (
        <Text style={styles.error} accessibilityRole="alert">
          {error}
        </Text>
      ) : null}

      {finalTexts.length > 0 || partialText ? (
        <View style={styles.transcriptBox}>
          <Text style={styles.transcriptTitle}>Live transcript</Text>
          {finalTexts.slice(-4).map((item, index) => (
            <Text key={String(finalTexts.length - 4 + index) + item} style={styles.finalText}>
              {item}
            </Text>
          ))}
          {partialText ? (
            <Text style={styles.partialText}>{partialText}</Text>
          ) : null}
        </View>
      ) : (
        <View style={styles.emptyTranscript}>
          <Text style={styles.emptyTranscriptText}>
            Your live transcript will appear here when you start speaking.
          </Text>
        </View>
      )}

      <Text style={styles.metrics}>
        {socketStatus === "open" && state === "recording"
          ? "Secure connection active"
          : state === "reconnecting"
            ? "Restoring connection"
            : "No active audio connection"}
        {state === "recording" ? " · " + chunkCount + " audio chunks sent" : ""}
        {state === "reconnecting"
          ? " · Attempt " + reconnectAttempt + "/" + MAX_RECONNECT_ATTEMPTS
          : ""}
      </Text>

      <View style={styles.actions}>
        {state === "recording" ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Pause conversation"
            style={styles.secondaryButton}
            onPress={() => void stopStreaming("pause")}
          >
            <Text style={styles.secondaryButtonText}>Pause</Text>
          </Pressable>
        ) : (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={
              lifecycleState === "paused" ? "Resume live conversation" : "Start live conversation"
            }
            style={[styles.primaryButton, !canStart && styles.disabled]}
            disabled={!canStart}
            onPress={() => void connectAndCapture(false)}
          >
            <Text style={styles.primaryButtonText}>
              {state === "connecting" || state === "requestingPermission"
                ? "Starting…"
                : state === "reconnecting"
                  ? "Reconnecting…"
                  : lifecycleState === "paused"
                    ? "Resume conversation"
                    : lifecycleState === "completed"
                      ? "Conversation finished"
                      : "Start live conversation"}
            </Text>
          </Pressable>
        )}

        {lifecycleState !== "completed" && lifecycleState !== "ready" && (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Finish conversation"
            style={styles.finishButton}
            disabled={state === "finishing"}
            onPress={() => {
              setState("finishing");
              void stopStreaming("finish");
            }}
          >
            <Text style={styles.finishButtonText}>Finish</Text>
          </Pressable>
        )}
      </View>

      {state === "error" && permissionMessage?.includes("device settings") ? (
        <Pressable
          accessibilityRole="button"
          style={styles.settingsButton}
          onPress={() => void Linking.openSettings()}
        >
          <Text style={styles.settingsButtonText}>Open device settings</Text>
        </Pressable>
      ) : null}

      <Text style={styles.privacy}>
        Audio is streamed securely and is not saved as a recording on this device. Streaming stops when you pause, finish, or leave the app.
      </Text>
    </View>
  );
}


function getSpeechErrorMessage(code: string): string {
  switch (code) {
    case "stt_provider_not_configured":
      return "Speech recognition is not configured for this environment.";
    case "speech_provider_unavailable":
      return "Speech recognition is temporarily unavailable. Reconnect and try again.";
    case "speech_invalid_provider_response":
      return "Speech recognition returned an invalid response. Start the microphone again.";
    case "speech_cancelled":
      return "Speech recognition was cancelled. Start the microphone again when ready.";
    case "speech_transcription_failed":
      return "Speech could not be transcribed. Start the microphone again.";
    default:
      return "The live speech service could not complete this request. Try again.";
  }
}

const styles = StyleSheet.create({
  panel: {
    gap: 12,
    padding: 16,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    backgroundColor: "#ffffff"
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8
  },
  headingBlock: { gap: 3 },
  eyebrow: {
    color: "#667085",
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 1.3
  },
  heading: {
    color: "#101828",
    fontSize: 20,
    fontWeight: "800"
  },
  status: {
    overflow: "hidden",
    paddingHorizontal: 10,
    paddingVertical: 6,
    borderRadius: 999,
    color: "#475467",
    backgroundColor: "#f2f4f7",
    fontSize: 12,
    fontWeight: "700"
  },
  statusActive: {
    color: "#067647",
    backgroundColor: "#ecfdf3"
  },
  statusError: {
    color: "#b42318",
    backgroundColor: "#fef3f2"
  },
  description: {
    color: "#475467",
    fontSize: 13,
    lineHeight: 19
  },
  formatRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    paddingVertical: 9,
    borderTopWidth: 1,
    borderBottomWidth: 1,
    borderColor: "#eaecf0"
  },
  formatLabel: { color: "#667085", fontSize: 12 },
  formatValue: { color: "#344054", fontSize: 12, fontWeight: "700" },
  notice: { color: "#175cd3", fontSize: 13, lineHeight: 18 },
  error: { color: "#b42318", fontSize: 13, lineHeight: 18 },
  transcriptBox: {
    gap: 7,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#f8fafc"
  },
  transcriptTitle: {
    color: "#344054",
    fontSize: 12,
    fontWeight: "800"
  },
  finalText: {
    color: "#101828",
    fontSize: 15,
    lineHeight: 21
  },
  partialText: {
    color: "#667085",
    fontSize: 15,
    fontStyle: "italic",
    lineHeight: 21
  },
  emptyTranscript: {
    borderWidth: 1,
    borderStyle: "dashed",
    borderColor: "#d0d5dd",
    borderRadius: 12,
    padding: 15
  },
  emptyTranscriptText: {
    color: "#667085",
    fontSize: 13,
    lineHeight: 19,
    textAlign: "center"
  },
  metrics: { color: "#667085", fontSize: 11 },
  actions: { flexDirection: "row", gap: 9 },
  primaryButton: {
    flex: 1,
    minHeight: 48,
    paddingHorizontal: 14,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  primaryButtonText: { color: "#ffffff", fontWeight: "800" },
  secondaryButton: {
    flex: 1,
    minHeight: 48,
    paddingHorizontal: 14,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#eef4ff"
  },
  secondaryButtonText: { color: "#175cd3", fontWeight: "800" },
  finishButton: {
    minWidth: 84,
    minHeight: 48,
    paddingHorizontal: 14,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    alignItems: "center",
    justifyContent: "center"
  },
  finishButtonText: { color: "#344054", fontWeight: "700" },
  disabled: { opacity: 0.5 },
  settingsButton: { minHeight: 44, justifyContent: "center" },
  settingsButtonText: { color: "#175cd3", fontWeight: "700" },
  privacy: { color: "#667085", fontSize: 11, lineHeight: 16 }
});
