import { useCallback, useEffect, useMemo, useState } from "react";
import {
  createConversation,
  deleteConversation,
  getConversationErrorMessage,
  listConversations,
  type ConversationSummary
} from "../services/api/conversationService";

interface ConversationSidebarProps {
  activeConversationId: string | null;
  onSelect: (conversation: ConversationSummary | null) => void;
}

const DEFAULT_INSTRUCTION =
  "During this conversation, translate English speech into natural and accurate Turkish. Unless I explicitly ask otherwise, only translate and explain what is being said.";

export function ConversationSidebar({
  activeConversationId,
  onSelect
}: ConversationSidebarProps) {
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [isReloading, setIsReloading] = useState(false);
  const [isCreating, setIsCreating] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [instruction, setInstruction] = useState(DEFAULT_INSTRUCTION);

  const loadConversations = useCallback(async (isReload = false) => {
    if (isReload) {
      setIsReloading(true);
    } else {
      setIsLoading(true);
    }

    setError(null);

    try {
      const page = await listConversations();
      setConversations(page.items);
      setNextCursor(page.nextCursor);
    } catch (loadError) {
      setError(getConversationErrorMessage(loadError));
    } finally {
      setIsLoading(false);
      setIsReloading(false);
    }
  }, []);

  async function loadMoreConversations() {
    if (!nextCursor || isLoadingMore || isLoading) {
      return;
    }

    setIsLoadingMore(true);
    setError(null);

    try {
      const page = await listConversations(nextCursor);
      setConversations((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (loadError) {
      setError(getConversationErrorMessage(loadError));
    } finally {
      setIsLoadingMore(false);
    }
  }

  useEffect(() => {
    void loadConversations();
  }, [loadConversations]);

  const sortedConversations = useMemo(
    () =>
      [...conversations].sort(
        (left, right) =>
          new Date(right.updatedAt).getTime() -
          new Date(left.updatedAt).getTime()
      ),
    [conversations]
  );

  async function handleCreate() {
    if (!title.trim() || !instruction.trim()) {
      return;
    }

    setIsCreating(true);
    setError(null);

    try {
      const created = await createConversation({
        title: title.trim(),
        instruction: instruction.trim(),
        sourceLanguage: "en",
        targetLanguage: "tr"
      });

      setConversations((current) => [created, ...current]);
      onSelect(created);
      setTitle("");
      setInstruction(DEFAULT_INSTRUCTION);
      setIsCreateOpen(false);
    } catch (createError) {
      setError(getConversationErrorMessage(createError));
    } finally {
      setIsCreating(false);
    }
  }

  async function handleDelete(conversation: ConversationSummary) {
    if (!window.confirm(`Delete "${conversation.title}"?`)) {
      return;
    }

    setDeletingId(conversation.id);
    setError(null);

    try {
      await deleteConversation(conversation.id);
      setConversations((current) =>
        current.filter((item) => item.id !== conversation.id)
      );

      if (activeConversationId === conversation.id) {
        onSelect(null);
      }
    } catch (deleteError) {
      setError(getConversationErrorMessage(deleteError));
    } finally {
      setDeletingId(null);
    }
  }

  return (
    <aside className="conversation-sidebar" aria-label="Conversations">
      <div className="sidebar-header">
        <div>
          <p className="sidebar-eyebrow">CONVERSATIONS</p>
          <h2>Chats</h2>
        </div>
        <button
          className="icon-button"
          type="button"
          onClick={() => setIsCreateOpen((current) => !current)}
          aria-label={isCreateOpen ? "Close new conversation form" : "New conversation"}
          title={isCreateOpen ? "Close" : "New conversation"}
        >
          {isCreateOpen ? "×" : "+"}
        </button>
      </div>

      {isCreateOpen && (
        <div className="create-panel">
          <label htmlFor="conversation-title">Title</label>
          <input
            id="conversation-title"
            value={title}
            onChange={(event) => setTitle(event.target.value)}
            maxLength={120}
            placeholder="Hotel check-in"
            disabled={isCreating}
          />

          <label htmlFor="conversation-instruction">Instruction</label>
          <textarea
            id="conversation-instruction"
            value={instruction}
            onChange={(event) => setInstruction(event.target.value)}
            maxLength={20000}
            rows={5}
            disabled={isCreating}
          />

          <button
            className="primary-button"
            type="button"
            onClick={() => void handleCreate()}
            disabled={isCreating || !title.trim() || !instruction.trim()}
          >
            {isCreating ? "Creating…" : "Create conversation"}
          </button>
        </div>
      )}

      <div className="sidebar-actions">
        <span className="conversation-count">
          {sortedConversations.length} conversation
          {sortedConversations.length === 1 ? "" : "s"}
        </span>
        <button
          className="text-button"
          type="button"
          onClick={() => void loadConversations(true)}
          disabled={isReloading || isLoading}
        >
          {isReloading ? "Refreshing…" : "Refresh"}
        </button>
      </div>

      {error && (
        <p className="sidebar-error" role="alert">
          {error}
        </p>
      )}

      <div className="conversation-list">
        {isLoading ? (
          <p className="sidebar-empty">Loading conversations…</p>
        ) : sortedConversations.length === 0 ? (
          <p className="sidebar-empty">
            No conversations yet. Create one to get started.
          </p>
        ) : (
          <>
            {sortedConversations.map((conversation) => (
            <div
              className={
                "conversation-item" +
                (activeConversationId === conversation.id
                  ? " conversation-item-active"
                  : "")
              }
              key={conversation.id}
            >
              <button
                className="conversation-select"
                type="button"
                onClick={() => onSelect(conversation)}
                aria-current={
                  activeConversationId === conversation.id ? "true" : undefined
                }
              >
                <span className="conversation-title">{conversation.title}</span>
                <span className="conversation-meta">
                  {conversation.sourceLanguage.toUpperCase()} →{" "}
                  {conversation.targetLanguage.toUpperCase()}
                </span>
              </button>
              <button
                className="delete-button"
                type="button"
                onClick={() => void handleDelete(conversation)}
                disabled={deletingId === conversation.id}
                aria-label={`Delete ${conversation.title}`}
                title="Delete conversation"
              >
                {deletingId === conversation.id ? "…" : "Delete"}
              </button>
            </div>
            ))}
            {nextCursor && (
              <button
                className="text-button"
                type="button"
                onClick={() => void loadMoreConversations()}
                disabled={isLoadingMore}
              >
                {isLoadingMore ? "Loading older conversations…" : "Load older conversations"}
              </button>
            )}
          </>
        )}
      </div>
    </aside>
  );
}
