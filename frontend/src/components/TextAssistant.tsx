import { useState, type FormEvent } from "react";
import {
  getAssistantErrorMessage,
  processAssistantInput,
  type AssistantInputKind
} from "../services/api/assistantService";
import type { ConversationMessage } from "../services/api/messageService";

interface TextAssistantProps {
  conversationId: string;
  onMessageCreated: (message: ConversationMessage) => void;
}

export function TextAssistant({
  conversationId,
  onMessageCreated
}: TextAssistantProps) {
  const [inputKind, setInputKind] =
    useState<AssistantInputKind>("heardSpeech");
  const [text, setText] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const normalizedText = text.trim();
  const canSubmit = normalizedText.length > 0 && !isSubmitting;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!canSubmit) {
      return;
    }

    setIsSubmitting(true);
    setError(null);

    try {
      const message = await processAssistantInput(conversationId, {
        inputKind,
        text: normalizedText
      });

      onMessageCreated(message);
      setText("");
    } catch (submitError: unknown) {
      setError(getAssistantErrorMessage(submitError));
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <section className="text-assistant" aria-labelledby="text-assistant-title">
      <div className="text-assistant-header">
        <div>
          <span className="workspace-kicker">TEXT ASSISTANT</span>
          <h3 id="text-assistant-title">Send a text turn</h3>
        </div>
      </div>

      <div
        className="assistant-mode-switcher"
        role="group"
        aria-label="Text input type"
      >
        <button
          type="button"
          className={
            inputKind === "heardSpeech"
              ? "assistant-mode assistant-mode-active"
              : "assistant-mode"
          }
          onClick={() => {
            setInputKind("heardSpeech");
            setError(null);
          }}
          disabled={isSubmitting}
        >
          English heard speech
        </button>
        <button
          type="button"
          className={
            inputKind === "userFormulationRequest"
              ? "assistant-mode assistant-mode-active"
              : "assistant-mode"
          }
          onClick={() => {
            setInputKind("userFormulationRequest");
            setError(null);
          }}
          disabled={isSubmitting}
        >
          Ask for an English formulation
        </button>
      </div>

      <form className="assistant-form" onSubmit={handleSubmit}>
        <label htmlFor="assistant-text">
          {inputKind === "heardSpeech"
            ? "What did the other person say?"
            : "What do you want to say in English?"}
        </label>
        <textarea
          id="assistant-text"
          value={text}
          onChange={(event) => {
            setText(event.target.value);
            setError(null);
          }}
          maxLength={8000}
          rows={4}
          disabled={isSubmitting}
          placeholder={
            inputKind === "heardSpeech"
              ? "Could you tell me where the station is?"
              : "İstasyona nasıl gideceğimi nasıl sorarım?"
          }
        />
        <div className="assistant-form-footer">
          <span className="assistant-helper">
            {inputKind === "heardSpeech"
              ? "Conversa will translate and explain the spoken English according to this conversation's instruction."
              : "Conversa will turn your Turkish request into a natural English formulation according to this conversation's instruction."}
          </span>
          <button
            className="primary-button assistant-submit"
            type="submit"
            disabled={!canSubmit}
          >
            {isSubmitting ? "Processing…" : "Send to assistant"}
          </button>
        </div>
      </form>

      {error && (
        <p className="assistant-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
