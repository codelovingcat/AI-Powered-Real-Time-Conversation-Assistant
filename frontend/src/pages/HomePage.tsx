import { useEffect, useState } from "react";
import { ConversationSidebar } from "../components/ConversationSidebar";
import { MessageHistory } from "../components/MessageHistory";
import { InstructionEditor } from "../components/InstructionEditor";
import { TextAssistant } from "../components/TextAssistant";
import { useAuth } from "../auth/AuthContext";
import type { ConversationSummary } from "../services/api/conversationService";
import {
  checkBackendHealth,
  type BackendHealth
} from "../services/api/healthService";

export function HomePage() {
  const { signOut } = useAuth();
  const [health, setHealth] = useState<BackendHealth | null>(null);
  const [latestMessage, setLatestMessage] = useState<import("../services/api/messageService").ConversationMessage | null>(null);
  const [activeConversation, setActiveConversation] =
    useState<ConversationSummary | null>(null);

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
          setActiveConversation(conversation);
          setLatestMessage(null);
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

            <TextAssistant
              conversationId={activeConversation.id}
              onMessageCreated={setLatestMessage}
            />

            <MessageHistory
              conversationId={activeConversation.id}
              newMessage={latestMessage}
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
      </section>
    </main>
  );
}
