import { useState } from "react";
import { copyText } from "../services/clipboard/copyText";

interface SuggestedAnswerActionsProps {
  suggestedAnswer: string;
  onRegenerate?: () => Promise<void>;
  isRegenerating?: boolean;
}

export function SuggestedAnswerActions({
  suggestedAnswer,
  onRegenerate,
  isRegenerating = false
}: SuggestedAnswerActionsProps) {
  const [copyState, setCopyState] = useState<"idle" | "copied" | "error">("idle");

  async function handleCopy() {
    try {
      await copyText(suggestedAnswer);
      setCopyState("copied");
    } catch {
      setCopyState("error");
    }
  }

  return (
    <div className="suggested-answer-actions">
      <button
        className="text-button suggested-answer-action"
        type="button"
        onClick={() => void handleCopy()}
        aria-label="Copy suggested English reply"
      >
        Copy English reply
      </button>

      {onRegenerate && (
        <button
          className="text-button suggested-answer-action"
          type="button"
          onClick={() => void onRegenerate()}
          disabled={isRegenerating}
          aria-label="Regenerate suggested English reply"
        >
          {isRegenerating ? "Regenerating…" : "Regenerate"}
        </button>
      )}

      <span className="suggested-answer-feedback" role="status" aria-live="polite">
        {copyState === "copied"
          ? "Copied."
          : copyState === "error"
            ? "Copy failed. Please try again."
            : ""}
      </span>
    </div>
  );
}
