import type { ConversationMessage } from "../services/api/messageService";
import { SuggestedAnswerActions } from "./SuggestedAnswerActions";

interface AiResultCardProps {
  message: ConversationMessage;
  onRegenerate?: () => Promise<void>;
  isRegenerating?: boolean;
  showHeader?: boolean;
}

export function AiResultCard({
  message,
  onRegenerate,
  isRegenerating = false,
  showHeader = true
}: AiResultCardProps) {
  const isUserRequest = message.role === "user";

  return (
    <article className={`ai-result-card${isUserRequest ? " ai-result-card-user" : ""}`}>
      {showHeader && (
        <header className="ai-result-header">
          <div>
            <span className="ai-result-kicker">
              {isUserRequest ? "YOUR REQUEST" : "HEARD SPEECH"}
            </span>
            <h4>{isUserRequest ? "English formulation" : "AI conversation result"}</h4>
          </div>
          <time dateTime={message.createdAt}>
            {formatMessageTime(message.createdAt)}
          </time>
        </header>
      )}

      <section className="ai-result-section ai-result-original">
        <span className="ai-result-label">
          {isUserRequest ? "What you asked" : "Original speech"}
        </span>
        <p>{message.originalText}</p>
      </section>

      {message.translation && (
        <section className="ai-result-section">
          <span className="ai-result-label">Turkish translation</span>
          <p>{message.translation}</p>
        </section>
      )}

      {message.explanation && (
        <section className="ai-result-section">
          <span className="ai-result-label">Explanation</span>
          <p>{message.explanation}</p>
        </section>
      )}

      {message.suggestedAnswer && (
        <section className="ai-result-suggestion">
          <div className="ai-result-suggestion-header">
            <span className="ai-result-label">
              {isUserRequest ? "Suggested English answer" : "Suggested answer"}
            </span>
            {message.responseType && (
              <span className="ai-result-type">
                {getResponseTypeLabel(message.responseType)}
              </span>
            )}
          </div>
          <p>{message.suggestedAnswer}</p>
          {message.suggestedAnswerTranslation && (
            <span className="ai-result-meaning">
              {message.suggestedAnswerTranslation}
            </span>
          )}
          <SuggestedAnswerActions
            suggestedAnswer={message.suggestedAnswer}
            onRegenerate={onRegenerate}
            isRegenerating={isRegenerating}
          />
        </section>
      )}

      {message.role === "speaker" && message.questionDetected && (
        <div className="ai-result-question" role="status">
          <span className="ai-result-question-indicator" aria-hidden="true" />
          <span>
            {message.questionDirectedAtUser
              ? "Question detected and directed at you."
              : "Question detected."}
          </span>
        </div>
      )}

      {message.role === "user" && !message.suggestedAnswer && (
        <p className="ai-result-empty">
          No formulation was returned for this request.
        </p>
      )}
    </article>
  );
}

function getResponseTypeLabel(
  responseType: ConversationMessage["responseType"]
): string {
  switch (responseType) {
    case "translation":
      return "Translation";
    case "question":
      return "Question";
    case "answer":
      return "Answer";
    case "instruction":
      return "Instruction";
    default:
      return "Result";
  }
}

function formatMessageTime(value: string): string {
  const date = new Date(value);

  if (Number.isNaN(date.getTime())) {
    return "";
  }

  return new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit"
  }).format(date);
}
