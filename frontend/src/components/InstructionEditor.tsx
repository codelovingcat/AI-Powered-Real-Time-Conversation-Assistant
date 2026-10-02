import { FormEvent, useEffect, useState } from "react";
import {
  getConversationErrorMessage,
  updateConversationInstruction,
  type ConversationSummary
} from "../services/api/conversationService";

interface InstructionEditorProps {
  conversation: ConversationSummary;
  onSaved: (conversation: ConversationSummary) => void;
}

const MAX_INSTRUCTION_LENGTH = 4000;

export function InstructionEditor({
  conversation,
  onSaved
}: InstructionEditorProps) {
  const [instruction, setInstruction] = useState(conversation.instruction);
  const [isSaving, setIsSaving] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setInstruction(conversation.instruction);
    setStatus(null);
    setError(null);
  }, [conversation.id, conversation.instruction]);

  const normalizedInstruction = instruction.trim();
  const hasChanges = normalizedInstruction !== conversation.instruction;
  const canSave =
    hasChanges && normalizedInstruction.length > 0 && !isSaving;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!canSave) {
      return;
    }

    setIsSaving(true);
    setStatus(null);
    setError(null);

    try {
      const updated = await updateConversationInstruction(
        conversation.id,
        normalizedInstruction
      );
      onSaved(updated);
      setInstruction(updated.instruction);
      setStatus("Instruction saved.");
    } catch (saveError: unknown) {
      setError(getConversationErrorMessage(saveError));
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <section className="instruction-editor" aria-labelledby="instruction-title">
      <div className="instruction-editor-header">
        <div>
          <span className="workspace-kicker">CONVERSATION INSTRUCTION</span>
          <h3 id="instruction-title">How should Conversa behave?</h3>
        </div>
        <span className="instruction-count">
          {instruction.length}/{MAX_INSTRUCTION_LENGTH}
        </span>
      </div>

      <form className="instruction-form" onSubmit={handleSubmit}>
        <label htmlFor="conversation-instruction-editor">
          Instruction
        </label>
        <textarea
          id="conversation-instruction-editor"
          value={instruction}
          onChange={(event) => {
            setInstruction(event.target.value);
            setStatus(null);
            setError(null);
          }}
          maxLength={MAX_INSTRUCTION_LENGTH}
          rows={5}
          disabled={isSaving}
          aria-describedby="instruction-help"
        />
        <div className="instruction-footer">
          <span id="instruction-help" className="instruction-help">
            This instruction is stored with this conversation and used for
            subsequent AI requests.
          </span>
          <button
            className="primary-button instruction-save-button"
            type="submit"
            disabled={!canSave}
          >
            {isSaving ? "Saving…" : "Save instruction"}
          </button>
        </div>
      </form>

      {status && (
        <p className="instruction-status" role="status">
          {status}
        </p>
      )}

      {error && (
        <p className="instruction-error" role="alert">
          {error}
        </p>
      )}
    </section>
  );
}
