import { useEffect, useRef, useState } from "react";
import { AiResultCard } from "./AiResultCard";
import {
  getMessageErrorMessage,
  listMessages,
  type ConversationMessage
} from "../services/api/messageService";

interface MessageHistoryProps {
  conversationId: string;
}

export function MessageHistory({ conversationId }: MessageHistoryProps) {
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const endRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    setMessages([]);
    setIsLoading(true);
    setError(null);

    void listMessages(conversationId, controller.signal)
      .then((items) => {
        if (!controller.signal.aborted) {
          setMessages(items);
        }
      })
      .catch((loadError: unknown) => {
        if (!controller.signal.aborted) {
          setError(getMessageErrorMessage(loadError));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) {
          setIsLoading(false);
        }
      });

    return () => controller.abort();
  }, [conversationId]);

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: "nearest" });
  }, [messages]);

  if (isLoading) {
    return (
      <section className="message-history" aria-labelledby="message-history-title">
        <div className="message-history-header">
          <div>
            <span className="workspace-kicker">MESSAGE HISTORY</span>
            <h3 id="message-history-title">Conversation</h3>
          </div>
        </div>
        <p className="message-history-state" aria-live="polite">
          Loading message history…
        </p>
      </section>
    );
  }

  return (
    <section className="message-history" aria-labelledby="message-history-title">
      <div className="message-history-header">
        <div>
          <span className="workspace-kicker">MESSAGE HISTORY</span>
          <h3 id="message-history-title">Conversation</h3>
        </div>
        <span className="message-count" aria-label={`${messages.length} messages`}>
          {messages.length}
        </span>
      </div>

      {error && (
        <div className="message-history-error" role="alert">
          {error}
        </div>
      )}

      {!error && messages.length === 0 ? (
        <div className="message-history-empty">
          <strong>No messages yet.</strong>
          <span>
            This conversation is ready. New turns will appear here as they are
            processed.
          </span>
        </div>
      ) : (
        <div className="message-stream" aria-live="polite">
          {messages.map((message) => (
            {message.role === "speaker" || message.role === "user" ? (
              <AiResultCard key={message.id} message={message} />
            ) : (
              <MessageBubble key={message.id} message={message} />
            )}
          ))}
          <div ref={endRef} aria-hidden="true" />
        </div>
      )}
    </section>
  );
}

function MessageBubble({ message }: { message: ConversationMessage }) {
  const roleLabel = getRoleLabel(message.role);

  return (
    <article className={`message-bubble message-role-${message.role}`}>
      <header className="message-bubble-header">
        <span>{roleLabel}</span>
        <time dateTime={message.createdAt}>{formatMessageTime(message.createdAt)}</time>
      </header>

      <p className="message-original">{message.originalText}</p>

      {message.translation && (
        <div className="message-detail">
          <span className="message-detail-label">Turkish</span>
          <p>{message.translation}</p>
        </div>
      )}

      {message.explanation && (
        <div className="message-detail">
          <span className="message-detail-label">Explanation</span>
          <p>{message.explanation}</p>
        </div>
      )}

      {message.suggestedAnswer && (
        <div className="message-detail message-suggestion">
          <span className="message-detail-label">Suggested answer</span>
          <p>{message.suggestedAnswer}</p>
          {message.suggestedAnswerTranslation && (
            <small>{message.suggestedAnswerTranslation}</small>
          )}
        </div>
      )}

      {message.role === "speaker" && message.questionDetected && (
        <div className="message-badge">
          {message.questionDirectedAtUser
            ? "Question directed at you"
            : "Question detected"}
        </div>
      )}
    </article>
  );
}

function getRoleLabel(role: ConversationMessage["role"]): string {
  switch (role) {
    case "speaker":
      return "Speaker";
    case "user":
      return "You";
    case "assistant":
      return "Assistant";
    default:
      return "System";
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
