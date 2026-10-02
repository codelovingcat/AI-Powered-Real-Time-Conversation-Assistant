import { useEffect, useState } from "react";
import { useAuth } from "../auth/AuthContext";
import {
  checkBackendHealth,
  type BackendHealth
} from "../services/api/healthService";

export function HomePage() {
  const { signOut } = useAuth();
  const [health, setHealth] = useState<BackendHealth | null>(null);

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
    <main className="page-shell">
      <section className="hero-card" aria-labelledby="page-title">
        <div className="top-row">
          <p className="eyebrow">REAL-TIME CONVERSATION ASSISTANT</p>
          <button className="ghost-button" type="button" onClick={signOut}>
            Sign out
          </button>
        </div>

        <h1 id="page-title">Conversa</h1>
        <p className="hero-copy">
          Your authenticated session is active. Conversation features will
          be connected in the next step.
        </p>

        <div className="status-card" role="status" aria-live="polite">
          <div>
            <span className="status-label">Backend status</span>
            <strong>
              {health?.status === "available"
                ? "Connected"
                : health?.status === "unavailable"
                  ? "Unavailable"
                  : "Checking…"}
            </strong>
            {health && <span className="status-detail">{health.detail}</span>}
          </div>
          <span
            className={"status-dot status-" + (health?.status ?? "checking")}
            aria-hidden="true"
          />
        </div>
      </section>
    </main>
  );
}
