export type ConversationStatusTone = "info" | "success" | "warning" | "error";

interface ConversationStatusProps {
  tone: ConversationStatusTone;
  title: string;
  detail?: string;
  actionLabel?: string;
  onAction?: () => void;
  busy?: boolean;
}

export function ConversationStatus({
  tone,
  title,
  detail,
  actionLabel,
  onAction,
  busy = false
}: ConversationStatusProps) {
  return (
    <aside className={`conversation-status conversation-status-${tone}`} role="status" aria-live="polite">
      <div className="conversation-status-indicator" aria-hidden="true" />
      <div className="conversation-status-content">
        <strong>{title}</strong>
        {detail && <span>{detail}</span>}
      </div>
      {actionLabel && onAction && (
        <button
          className="text-button conversation-status-action"
          type="button"
          onClick={onAction}
          disabled={busy}
        >
          {busy ? "Trying…" : actionLabel}
        </button>
      )}
    </aside>
  );
}
