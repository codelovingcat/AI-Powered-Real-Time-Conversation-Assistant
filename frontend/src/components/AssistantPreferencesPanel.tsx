import { useEffect, useState, type FormEvent } from "react";
import {
  DEFAULT_ASSISTANT_PREFERENCES,
  getAssistantPreferences,
  saveAssistantPreferences,
  type AssistantPreferences,
  type AssistantTone,
  type ExplanationLevel,
  type ResponseLength
} from "../services/api/assistantPreferencesService";

interface AssistantPreferencesPanelProps {
  onClose: () => void;
}

export function AssistantPreferencesPanel({
  onClose
}: AssistantPreferencesPanelProps) {
  const [preferences, setPreferences] = useState<AssistantPreferences>(
    DEFAULT_ASSISTANT_PREFERENCES
  );
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    const controller = new AbortController();

    void getAssistantPreferences(controller.signal)
      .then((loaded) => {
        setPreferences(loaded);
        setError(null);
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setError("Preferences could not be loaded. Check the connection and try again.");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setLoading(false);
        }
      });

    return () => controller.abort();
  }, []);

  function updatePreference<K extends keyof AssistantPreferences>(
    key: K,
    value: AssistantPreferences[K]
  ) {
    setPreferences((current) => ({ ...current, [key]: value }));
    setSaved(false);
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    setSaved(false);

    try {
      const persisted = await saveAssistantPreferences(preferences);
      setPreferences(persisted);
      setSaved(true);
    } catch {
      setError("Preferences could not be saved. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="preferences-backdrop">
      <section
        className="preferences-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="assistant-preferences-title"
      >
        <div className="preferences-header">
          <div>
            <p className="workspace-kicker">PERSONALIZE CONVERSA</p>
            <h2 id="assistant-preferences-title">Assistant preferences</h2>
          </div>
          <button
            className="ghost-button"
            type="button"
            onClick={onClose}
            aria-label="Close assistant preferences"
          >
            Close
          </button>
        </div>

        <p className="preferences-copy">
          These preferences are saved to your account and used for both text
          requests and live conversation assistance. Conversation instructions
          and safety rules still take priority.
        </p>

        {loading ? (
          <p className="preferences-state" role="status">
            Loading your preferences…
          </p>
        ) : (
          <form className="preferences-form" onSubmit={handleSubmit}>
            <label>
              Response length
              <select
                value={preferences.responseLength}
                onChange={(event) =>
                  updatePreference(
                    "responseLength",
                    event.target.value as ResponseLength
                  )
                }
                disabled={saving}
              >
                <option value="concise">Concise</option>
                <option value="balanced">Balanced (default)</option>
                <option value="detailed">Detailed</option>
              </select>
              <span>Choose how compact or detailed the overall response should be.</span>
            </label>

            <label>
              Tone
              <select
                value={preferences.tone}
                onChange={(event) =>
                  updatePreference("tone", event.target.value as AssistantTone)
                }
                disabled={saving}
              >
                <option value="casual">Casual</option>
                <option value="contextual">Follow the conversation (default)</option>
                <option value="formal">Formal</option>
              </select>
              <span>The selected tone never changes the meaning of what was said.</span>
            </label>

            <label>
              Explanation detail
              <select
                value={preferences.explanationLevel}
                onChange={(event) =>
                  updatePreference(
                    "explanationLevel",
                    event.target.value as ExplanationLevel
                  )
                }
                disabled={saving}
              >
                <option value="minimal">Minimal</option>
                <option value="balanced">Balanced (default)</option>
                <option value="detailed">Detailed</option>
              </select>
              <span>Control how much extra context is provided beyond the translation.</span>
            </label>

            <label className="preferences-checkbox">
              <input
                type="checkbox"
                checked={preferences.suggestReplies}
                onChange={(event) =>
                  updatePreference("suggestReplies", event.target.checked)
                }
                disabled={saving}
              />
              <span>
                Suggest replies when someone is speaking to me
                <small>
                  Turning this off hides unsolicited reply suggestions, but you
                  can still explicitly ask Conversa to formulate something to say.
                </small>
              </span>
            </label>

            {error && <p className="auth-error" role="alert">{error}</p>}
            {saved && (
              <p className="preferences-success" role="status">
                Preferences saved. They will be used in future AI requests.
              </p>
            )}

            <div className="preferences-actions">
              <button
                className="ghost-button"
                type="button"
                onClick={onClose}
                disabled={saving}
              >
                Cancel
              </button>
              <button
                className="primary-button"
                type="submit"
                disabled={saving || loading}
              >
                {saving ? "Saving…" : "Save preferences"}
              </button>
            </div>
          </form>
        )}

        {!loading && error && (
          <div className="preferences-retry">
            <button
              className="ghost-button"
              type="button"
              onClick={() => window.location.reload()}
            >
              Reload page
            </button>
          </div>
        )}
      </section>
    </div>
  );
}
