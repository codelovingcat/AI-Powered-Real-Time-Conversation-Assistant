import { useState, type FormEvent } from "react";
import {
  getAssistantErrorMessage,
  processAssistantInput,
  type AssistantInputKind
} from "../services/api/assistantService";

interface TextAssistantPanelProps {
  conversationId: string | null;
  onProcessed?: () => void;
}

const MODES: Array<{
  value: AssistantInputKind;
  label: string;
  title: string;
  placeholder: string;
  help: string;
}> = [
  {
    value: "heardSpeech",
    label: "English speech",
    title: "What did they say?",
    placeholder: "Type the English sentence you heard…",
    help: "Conversa will translate and explain the sentence, and detect whether it is a question directed at you."
  },
  {
    value: "userFormulationRequest",
    label: "Turkish request",
    title: "What do you want to say?",
    placeholder: "Türkçe olarak ne söylemek istediğini yaz…",
    help: "Conversa will turn your Turkish request into a natural English response."
  }
];

export function TextAssistantPanel({
  conversationId,
  onProcessed
}: TextAssistantPanelProps) {
  const [inputKind, setInputKind] =
    useState<AssistantInputKind>("heardSpeech");
  const [text, setText] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mode = MODES.find((item) => item.value === inputKind) ?? MODES[0];

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const trimmedText = text.trim();

    if (!conversationId || !trimmedText || isSubmitting) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      await processAssistantInput(conversationId, inputKind, trimmedText);
      setText("");
      onProcessed?.();
    } catch (submitError: unknown) {
      setError(getAssistantErrorMessage(submitError));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <section className="text-assistant-panel" aria-labelledby="text-assistant-title">
      <div className="text-assistant-header">
        <div>
          <span className="workspace-kicker">TEXT ASSISTANT</span>
          <h3 id="text-assistant-title">Try a conversation turn</h3>
        </div>
        <span className="text-assistant-badge">Gemini</span>
      </div>

      <div className="text-assistant-mode" role="tablist" aria-label="Assistant input type">
        {MODES.map((item) => (
          <button
            key={item.value}
            className={
              "text-assistant-mode-button" +
              (inputKind === item.value ? " text-assistant-mode-active" : "")
            }
            type="button"
            role="tab"
            aria-selected={inputKind === item.value}
            onClick={() => {
              setInputKind(item.value);
              setError(null);
            }}
            disabled={isSubmitting}
          >
            {item.label}
          </button>
        ))}
      </div>

      <form className="text-assistant-form" onSubmit={handleSubmit}>
        <label htmlFor="assistant-text-input">{mode.title}</label>
        <textarea
          id="assistant-text-input"
          value={text}
          onChange={(event) => setText(event.target.value)}
          placeholder={mode.placeholder}
          maxLength={10000}
          rows={5}
          disabled={!conversationId || isSubmitting}
        />

        <div className="text-assistant-footer">
          <p>{mode.help}</p>
          <button
            className="primary-button text-assistant-submit"
            type="submit"
            disabled={!conversationId || !text.trim() || isSubmitting}
          >
            {isSubmitting ? "Processing…" : "Send to assistant"}
          </button>
        </div>
      </form>

      {!conversationId && (
        <p className="text-assistant-note">
          Select a conversation before sending text to the assistant.
        </p>
      )}

      {error && (
        <p className="text-assistant-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
