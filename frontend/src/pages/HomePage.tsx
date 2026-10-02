import { useEffect, useState } from "react";
import { ConversationSidebar } from "../components/ConversationSidebar";
import { ConversationStatus } from "../components/ConversationStatus";
import { MicrophoneCapturePanel } from "../components/MicrophoneCapturePanel";
import { TextAssistantPanel } from "../components/TextAssistantPanel";
import { MessageHistory } from "../components/MessageHistory";
import { InstructionEditor } from "../components/InstructionEditor";
import { useAuth } from "../auth/AuthContext";
import type { ConversationSummary } from "../services/api/conversationService";
import {
  getAssistantErrorMessage,
  processAssistantInput,
  type AssistantInputKind
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
  const [textInputKind, setTextInputKind] =
    useState<AssistantInputKind>("heardSpeech");
  const [textInput, setTextInput] = useState("");
  const [isTextSubmitting, setIsTextSubmitting] = useState(false);
  const [textInputError, setTextInputError] = useState<string | null>(null);

  async function handleFinalTranscript(text: string) {
    if (!activeConversation || !text.trim()) {
      return;
    }

    setIsAiLoading(true);
    setAiError(null);

    try {
      await processAssistantInput(
        activeConversation.id,
        "heardSpeech",
        text.trim()
      );
      setRefreshToken((current) => current + 1);
    } catch (error: unknown) {
      setAiError(getAssistantErrorMessage(error));
    } finally {
      setIsAiLoading(false);
    }
  }

  async function handleTextSubmit() {
    const trimmedText = textInput.trim();

    if (!activeConversation || !trimmedText || isTextSubmitting) {
      return;
    }

    setIsTextSubmitting(true);
    setTextInputError(null);
    setAiError(null);

    try {
      await processAssistantInput(
        activeConversation.id,
        textInputKind,
        trimmedText
      );
      setTextInput("");
      setRefreshToken((current) => current + 1);
    } catch (error: unknown) {
      setTextInputError(getAssistantErrorMessage(error));
    } finally {
      setIsTextSubmitting(false);
    }
  }

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
        onSelect={(conversation) => {
          setAiError(null);
          setIsAiLoading(false);
          setActiveConversation(conversation);
        }}
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
        />
      </section>
    </main>
  );
}
