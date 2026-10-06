import { useEffect, useRef, useState } from "react";
import { AiResultCard } from "./AiResultCard";
import { SuggestedAnswerActions } from "./SuggestedAnswerActions";
import { processAssistantInput } from "../services/api/assistantService";
import { getAssistantErrorMessage } from "../services/api/assistantService";
import {
  getLiveFinalText,
  shouldRenderLivePartial,
  type LiveConnectionState,
  type LiveTimelineSnapshot
} from "../services/audio/conversationTimelineState";
import type { TranscriptSnapshot } from "../services/audio/transcriptState";
import {
  getMessageErrorMessage,
  listMessages,
  type ConversationMessage
} from "../services/api/messageService";

interface MessageHistoryProps {
  conversationId: string;
  refreshToken?: number;
  liveTranscript?: TranscriptSnapshot;
  liveConnectionState?: LiveConnectionState;
  latestAiResult?: ConversationMessage | null;
  onAiResultRegenerated?: (message: ConversationMessage) => void;
  onHistorySynchronized?: () => void;
}

export function MessageHistory({
  conversationId,
  refreshToken = 0,
  liveTranscript,
  liveConnectionState = "idle",
  latestAiResult = null,
  onAiResultRegenerated,
  onHistorySynchronized
}: MessageHistoryProps) {
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingOlder, setIsLoadingOlder] = useState(false);
  const [regeneratingMessageId, setRegeneratingMessageId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const endRef = useRef<HTMLDivElement | null>(null);
  const streamRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setMessages([]);
    setNextCursor(null);
    setError(null);
    setIsLoading(true);
  }, [conversationId]);

  useEffect(() => {
    const controller = new AbortController();

    setIsLoading(true);
    setError(null);

    void listMessages(conversationId, undefined, controller.signal)
      .then((page) => {
        if (!controller.signal.aborted) {
          setMessages(page.items);
          setNextCursor(page.nextCursor);
          onHistorySynchronized?.();
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
  }, [conversationId, refreshToken, onHistorySynchronized]);

  const timelineSnapshot: LiveTimelineSnapshot = {
    transcript: liveTranscript ?? {
      finalTexts: [],
      partialText: "",
      confidence: null
    },
    latestAiResult
  };
  const liveFinalText = getLiveFinalText(timelineSnapshot);
  const showLivePartial = shouldRenderLivePartial(timelineSnapshot);
  const liveHasContent = Boolean(liveFinalText || showLivePartial);
  const hasTimelineContent =
    messages.length > 0 || liveHasContent || Boolean(latestAiResult);

  useEffect(() => {
    if (!isLoading) {
      endRef.current?.scrollIntoView({ block: "nearest" });
    }
  }, [isLoading]);

  async function handleRegenerate(message: ConversationMessage) {
    if (!message.suggestedAnswer || regeneratingMessageId !== null) {
      return;
    }

    const inputKind =
      message.role === "user" ? "userFormulationRequest" : "heardSpeech";
    const isLiveResult = latestAiResult?.id === message.id;

    setRegeneratingMessageId(message.id);
    setError(null);

    try {
      const regenerated = await processAssistantInput(
        message.conversationId,
        inputKind,
        message.originalText
      );

      if (isLiveResult) {
        onAiResultRegenerated?.(regenerated);
        return;
      }

      setMessages((current) => [
        ...current,
        ...(
          current.some((item) => item.id === regenerated.id)
            ? []
            : [regenerated]
        )
      ]);
    } catch (regenerateError: unknown) {
      setError(getAssistantErrorMessage(regenerateError));
    } finally {
      setRegeneratingMessageId(null);
    }
  }

  async function loadOlderMessages() {
    if (!nextCursor || isLoadingOlder || isLoading) {
      return;
    }

    const stream = streamRef.current;
    const previousHeight = stream?.scrollHeight ?? 0;
    const previousTop = stream?.scrollTop ?? 0;

    setIsLoadingOlder(true);
    setError(null);

    try {
      const page = await listMessages(conversationId, nextCursor);
      setMessages((current) => [...page.items, ...current]);
      setNextCursor(page.nextCursor);

      requestAnimationFrame(() => {
        if (!stream) {
          return;
        }

        stream.scrollTop =
          stream.scrollHeight - previousHeight + previousTop;
      });
    } catch (loadError: unknown) {
      setError(getMessageErrorMessage(loadError));
    } finally {
      setIsLoadingOlder(false);
    }
  }

  if (isLoading && messages.length === 0 && !hasTimelineContent) {
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
        <span className="message-count" aria-label={`${messages.length} messages loaded`}>
          {messages.length}
        </span>
      </div>

      {error && (
        <div className="message-history-error" role="alert">
          {error}
        </div>
      )}

      {!error && !hasTimelineContent ? (
        <div className="message-history-empty">
          <strong>No messages yet.</strong>
          <span>
            This conversation is ready. New turns will appear here as they are
            processed.
          </span>
        </div>
      ) : (
        <>
          <LiveConnectionIndicator state={liveConnectionState} />

          {nextCursor && (
            <button
              className="text-button message-history-load-more"
              type="button"
              onClick={() => void loadOlderMessages()}
              disabled={isLoadingOlder}
            >
              {isLoadingOlder ? "Loading older messages…" : "Load older messages"}
            </button>
          )}

          <div className="message-stream" ref={streamRef} aria-live="polite">
            {messages.map((message) => {
              if (latestAiResult?.id === message.id) {
                return null;
              }

              return message.role === "speaker" || message.role === "user" ? (
                <AiResultCard
                  key={message.id}
                  message={message}
                  onRegenerate={
                    message.suggestedAnswer
                      ? () => handleRegenerate(message)
                      : undefined
                  }
                  isRegenerating={regeneratingMessageId === message.id}
                />
              ) : (
                <MessageBubble key={message.id} message={message} />
              );
            })}

            {showLivePartial && (
              <LiveTranscriptTurn
                text={timelineSnapshot.transcript.partialText}
                final={false}
              />
            )}

            {liveFinalText && (
              <LiveTranscriptTurn
                text={liveFinalText}
                final
              />
            )}

            {latestAiResult && (
              <LiveAiAssistanceCard message={latestAiResult} />
            )}

            <div ref={endRef} aria-hidden="true" />
          </div>
        </>
      )}
    </section>
  );
}

function LiveConnectionIndicator({
  state
}: {
  state: LiveConnectionState;
}) {
  if (
    state === "idle" ||
    state === "connected"
  ) {
    return null;
  }

  const content = (() => {
    switch (state) {
      case "connecting":
        return ["Connecting live audio…", "The timeline will continue updating when the connection opens."];
      case "reconnecting":
        return ["Reconnecting live audio…", "Your conversation history remains intact while the connection is restored."];
      case "error":
        return ["Live audio connection unavailable", "The timeline remains available and no saved messages are removed."];
      case "disconnected":
        return ["Live audio disconnected", "Start the microphone again to continue the live turn."];
      default:
        return null;
    }
  })();

  if (!content) {
    return null;
  }

  const tone = state === "error" ? "error" : state === "reconnecting" ? "warning" : "info";

  return (
    <div
      className={"timeline-connection timeline-connection-" + tone}
      role={state === "error" ? "alert" : "status"}
      aria-live={state === "error" ? "assertive" : "polite"}
    >
      <span className="timeline-connection-dot" aria-hidden="true" />
      <div>
        <strong>{content[0]}</strong>
        <span>{content[1]}</span>
      </div>
    </div>
  );
}

function LiveTranscriptTurn({
  text,
  final
}: {
  text: string;
  final: boolean;
}) {
  return (
    <article className={"timeline-live-turn" + (final ? " timeline-live-turn-final" : " timeline-live-turn-partial")}>
      <header className="timeline-live-header">
        <span>HEARD SPEECH</span>
        <span>{final ? "Final" : "Partial"}</span>
      </header>
      <p>{text}</p>
    </article>
  );
}

function LiveAiAssistanceCard({
  message
}: {
  message: ConversationMessage;
}) {
  return (
    <article className="timeline-ai-assistance">
      <header>
        <span>AI ASSISTANCE</span>
        <time dateTime={message.createdAt}>{formatMessageTime(message.createdAt)}</time>
      </header>

      {message.translation && (
        <section>
          <span>Turkish translation</span>
          <p>{message.translation}</p>
        </section>
      )}

      {message.explanation && (
        <section>
          <span>Explanation</span>
          <p>{message.explanation}</p>
        </section>
      )}

      {message.suggestedAnswer && (
        <section>
          <span>Suggested answer</span>
          <p>{message.suggestedAnswer}</p>
          {message.suggestedAnswerTranslation && (
            <small>{message.suggestedAnswerTranslation}</small>
          )}
          <SuggestedAnswerActions
            suggestedAnswer={message.suggestedAnswer}
            onRegenerate={
              () => handleRegenerate(message)
            }
            isRegenerating={regeneratingMessageId === message.id}
          />
        </section>
      )}

      {message.role === "speaker" && message.questionDetected && (
        <div className="timeline-ai-question">
          {message.questionDirectedAtUser
            ? "Question detected and directed at you."
            : "Question detected."}
        </div>
      )}
    </article>
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
