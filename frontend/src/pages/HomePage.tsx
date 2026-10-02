import { useEffect, useState } from "react";
import { ConversationSidebar } from "../components/ConversationSidebar";
import { useAuth } from "../auth/AuthContext";
import type { ConversationSummary } from "../services/api/conversationService";
import {
  checkBackendHealth,
  type BackendHealth
} from "../services/api/healthService";

export function HomePage() {
  const { signOut } = useAuth();
  const [health, setHealth] = useState<BackendHealth | null>(null);
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
        onSelect={setActiveConversation}
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

        <div className="workspace-card">
          {activeConversation ? (
            <>
              <span className="workspace-kicker">ACTIVE CONVERSATION</span>
              <h2>{activeConversation.title}</h2>
              <p>
                {activeConversation.sourceLanguage.toUpperCase()} →{" "}
                {activeConversation.targetLanguage.toUpperCase()}
              </p>
              <div className="status-card" role="status">
                <div>
                  <span className="status-label">Backend status</span>
                  <strong>
                    {health?.status === "available"
                      ? "Connected"
                      : health?.status === "unavailable"
                        ? "Unavailable"
                        : "Checking…"}
                  </strong>
                  {health && (
                    <span className="status-detail">{health.detail}</span>
                  )}
                </div>
                <span
                  className={
                    "status-dot status-" + (health?.status ?? "checking")
                  }
                  aria-hidden="true"
                />
              </div>
              <p className="workspace-note">
                Message history and conversation editing will be connected in
                the next steps.
              </p>
            </>
          ) : (
            <>
              <span className="workspace-kicker">GET STARTED</span>
              <h2>Select a conversation</h2>
              <p>
                Choose a conversation from the sidebar or create a new one to
                continue.
              </p>
            </>
          )}
        </div>
      </section>
    </main>
  );
}
