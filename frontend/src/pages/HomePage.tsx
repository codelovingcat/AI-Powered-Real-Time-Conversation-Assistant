import { useCallback, useEffect, useState } from "react";
import { ConversationSidebar } from "../components/ConversationSidebar";
import { ConversationStatus } from "../components/ConversationStatus";
import { MicrophoneCapturePanel } from "../components/MicrophoneCapturePanel";
import { TextAssistantPanel } from "../components/TextAssistantPanel";
import { MessageHistory } from "../components/MessageHistory";
import { InstructionEditor } from "../components/InstructionEditor";
import { EMPTY_TRANSCRIPT, type TranscriptSnapshot } from "../services/audio/transcriptState";
import {
  type LiveConnectionState
} from "../services/audio/conversationTimelineState";
import type { ConversationMessage } from "../services/api/messageService";
import { useAuth } from "../auth/AuthContext";
import type { ConversationSummary } from "../services/api/conversationService";
import {
  getAssistantErrorMessage,
  processAssistantInput
} from "../services/api/assistantService";
import {
  checkBackendHealth,
  type BackendHealth
} from "../services/api/healthService";

export function HomePage() {
  const { signOut } = useAuth();
  const [health, setHealth] = useState<BackendHealth | null>(null);
  const [activeConversation, setActiveConversation] =
    useState<ConversationSummary | null>(null);
  const [isAiLoading, setIsAiLoading] = useState(false);
  const [aiError, setAiError] = useState<string | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);
  const [liveTranscript, setLiveTranscript] =
    useState<TranscriptSnapshot>(EMPTY_TRANSCRIPT);
  const [liveConnectionState, setLiveConnectionState] =
    useState<LiveConnectionState>("idle");
  const [latestAiResult, setLatestAiResult] =
    useState<ConversationMessage | null>(null);

  async function handleFinalTranscript(text: string) {
    if (!activeConversation || !text.trim()) {
      return;
    }

    setLatestAiResult(null);
    setIsAiLoading(true);
    setAiError(null);

    try {
      const result = await processAssistantInput(
        activeConversation.id,
        "heardSpeech",
        text.trim()
      );
      setLatestAiResult(result);
      setRefreshToken((current) => current + 1);
    } catch (error: unknown) {
      setAiError(getAssistantErrorMessage(error));
    } finally {
      setIsAiLoading(false);
    }
  }

  function handleLiveTranscriptChange(nextTranscript: TranscriptSnapshot) {
    if (
      nextTranscript.partialText.trim() ||
      nextTranscript.finalTexts.length === 0
    ) {
      setLatestAiResult(null);
    }

    setLiveTranscript(nextTranscript);
  }

  function handleConversationSelect(
    conversation: ConversationSummary | null
  ) {
    setAiError(null);
    setIsAiLoading(false);
    setLiveTranscript(EMPTY_TRANSCRIPT);
    setLatestAiResult(null);
    setLiveConnectionState("idle");
    setActiveConversation(conversation);
  }

  const handleHistorySynchronized = useCallback(() => {
    setLiveTranscript(EMPTY_TRANSCRIPT);
    setLatestAiResult(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();

    void checkBackendHealth(controller.signal)
      .then(setHealth)
      .catch(() => {
        if (!controller.signal.aborted) {
          setHealth({
            status: "unavailable",
            detail: "Backend connection is unavailable."
          });
        }
      });

    return () => controller.abort();
  }, []);

  return (
    <main className="app-shell">
      <ConversationSidebar
        activeConversationId={activeConversation?.id ?? null}
        onSelect={handleConversationSelect}
      />

      <section className="workspace" aria-labelledby="page-title">
        <header className="workspace-header">
          <div>
            <p className="eyebrow">REAL-TIME CONVERSATION ASSISTANT</p>
            <h1 id="page-title">Conversa</h1>
          </div>
          <button className="ghost-button" type="button" onClick={signOut}>
            Sign out
          </button>
        </header>

        {health?.status === "unavailable" && (
          <ConversationStatus
            tone="warning"
            title="Backend unavailable"
            detail="Conversa cannot reach the API right now. Check the connection and try again."
          />
        )}

        {aiError && (
          <ConversationStatus
            tone="error"
            title="AI response unavailable"
            detail={aiError}
          />
        )}

        {isAiLoading && (
          <ConversationStatus
            tone="info"
            title="AI is processing"
            detail="Your transcript was received. Translating and preparing conversation help…"
          />
        )}

        {activeConversation ? (
          <div className="workspace-card">
            <div className="active-conversation-header">
              <div>
                <span className="workspace-kicker">ACTIVE CONVERSATION</span>
                <h2>{activeConversation.title}</h2>
                <p>
                  {activeConversation.sourceLanguage.toUpperCase()} →{" "}
                  {activeConversation.targetLanguage.toUpperCase()}
                </p>
              </div>
              <div className="status-compact" role="status">
                <span
                  className={
                    "status-dot status-" + (health?.status ?? "checking")
                  }
                  aria-hidden="true"
                />
                <span>
                  {health?.status === "available"
                    ? "Connected"
                    : health?.status === "unavailable"
                      ? "Unavailable"
                      : "Checking…"}
                </span>
              </div>
            </div>

            <InstructionEditor
              conversation={activeConversation}
              onSaved={setActiveConversation}
            />

            <MessageHistory
              conversationId={activeConversation.id}
              refreshToken={refreshToken}
              liveTranscript={liveTranscript}
              liveConnectionState={liveConnectionState}
              latestAiResult={latestAiResult}
              onHistorySynchronized={handleHistorySynchronized}
            />
          </div>
        ) : (
          <div className="workspace-card">
            <span className="workspace-kicker">GET STARTED</span>
            <h2>Select a conversation</h2>
            <p>
              Choose a conversation from the sidebar or create a new one to
              continue.
            </p>
          </div>
        )}

        <TextAssistantPanel
          conversationId={activeConversation?.id ?? null}
          onProcessed={() => setRefreshToken((current) => current + 1)}
        />

        <MicrophoneCapturePanel
          conversationId={activeConversation?.id ?? null}
          onFinalTranscript={handleFinalTranscript}
          onTranscriptChange={handleLiveTranscriptChange}
          onConnectionStateChange={setLiveConnectionState}
        />
      </section>
    </main>
  );
}
