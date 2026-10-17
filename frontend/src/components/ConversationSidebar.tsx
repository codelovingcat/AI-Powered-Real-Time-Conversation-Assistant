import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ApiError } from "../services/api/apiClient";
import "./ConversationSidebar.metadata.css";
import {
  createConversation,
  deleteConversation,
  getConversation,
  getConversationErrorMessage,
  listConversations,
  updateConversationTitle,
  type ConversationListFilters,
  type ConversationSummary
} from "../services/api/conversationService";

interface ConversationSidebarProps {
  activeConversationId: string | null;
  onSelect: (conversation: ConversationSummary | null) => void;
  onConversationUpdated: (conversation: ConversationSummary) => void;
}

const DEFAULT_INSTRUCTION =
  "During this conversation, translate English speech into natural and accurate Turkish. Unless I explicitly ask otherwise, only translate and explain what is being said.";

function startOfDayIso(value: string): string {
  return new Date(value + "T00:00:00.000Z").toISOString();
}

function endOfDayExclusiveIso(value: string): string {
  const date = new Date(value + "T00:00:00.000Z");
  date.setUTCDate(date.getUTCDate() + 1);
  return date.toISOString();
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Unknown";
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(date);
}

export function ConversationSidebar({
  activeConversationId,
  onSelect,
  onConversationUpdated
}: ConversationSidebarProps) {
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [isReloading, setIsReloading] = useState(false);
  const [isCreating, setIsCreating] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [isLoadingTitleId, setIsLoadingTitleId] = useState<string | null>(null);
  const [editingConversation, setEditingConversation] = useState<ConversationSummary | null>(null);
  const [titleDraft, setTitleDraft] = useState("");
  const [isSavingTitle, setIsSavingTitle] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isCreateOpen, setIsCreateOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [instruction, setInstruction] = useState(DEFAULT_INSTRUCTION);
  const [searchDraft, setSearchDraft] = useState("");
  const [updatedFromDraft, setUpdatedFromDraft] = useState("");
  const [updatedToDraft, setUpdatedToDraft] = useState("");
  const [filters, setFilters] = useState<ConversationListFilters>({});

  const loadConversations = useCallback(
    async (filtersToApply: ConversationListFilters, isReload = false) => {
      if (isReload) {
        setIsReloading(true);
      } else {
        setIsLoading(true);
      }

      setError(null);

      try {
        const page = await listConversations(undefined, undefined, filtersToApply);
        setConversations(page.items);
        setNextCursor(page.nextCursor);
      } catch (loadError) {
        setError(getConversationErrorMessage(loadError));
      } finally {
        setIsLoading(false);
        setIsReloading(false);
      }
    },
    []
  );

  async function loadMoreConversations() {
    if (!nextCursor || isLoadingMore || isLoading || isReloading) {
      return;
    }

    setIsLoadingMore(true);
    setError(null);

    try {
      const page = await listConversations(nextCursor, undefined, filters);
      setConversations((current) => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    } catch (loadError) {
      setError(getConversationErrorMessage(loadError));
    } finally {
      setIsLoadingMore(false);
    }
  }

  useEffect(() => {
    void loadConversations({});
  }, [loadConversations]);

  const sortedConversations = conversations;
  const hasActiveFilters = Boolean(
    filters.search || filters.updatedFrom || filters.updatedTo
  );

  function buildFilters(): ConversationListFilters {
    return {
      search: searchDraft.trim() || undefined,
      updatedFrom: updatedFromDraft ? startOfDayIso(updatedFromDraft) : undefined,
      updatedTo: updatedToDraft ? endOfDayExclusiveIso(updatedToDraft) : undefined
    };
  }

  function handleSearch() {
    if (
      updatedFromDraft &&
      updatedToDraft &&
      updatedFromDraft > updatedToDraft
    ) {
      setError("Updated from must be earlier than or equal to updated to.");
      return;
    }

    const nextFilters = buildFilters();
    setFilters(nextFilters);
    void loadConversations(nextFilters, true);
  }

  function handleClearSearch() {
    setSearchDraft("");
    setUpdatedFromDraft("");
    setUpdatedToDraft("");
    setFilters({});
    void loadConversations({}, true);
  }

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

      if (hasActiveFilters) {
        await loadConversations(filters, true);
      } else {
        setConversations((current) => [created, ...current]);
      }
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

  function replaceConversation(updated: ConversationSummary) {
    setConversations((current) =>
      current.map((item) => item.id === updated.id ? updated : item)
    );
    onConversationUpdated(updated);
  }

  async function handleStartRename(conversation: ConversationSummary) {
    if (isSavingTitle || isLoadingTitleId) return;
    setIsLoadingTitleId(conversation.id);
    setError(null);

    try {
      const latest = await getConversation(conversation.id);
      replaceConversation(latest);
      setEditingConversation(latest);
      setTitleDraft(latest.title);
    } catch (loadError) {
      setError(getConversationErrorMessage(loadError));
    } finally {
      setIsLoadingTitleId(null);
    }
  }

  async function handleSaveTitle(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!editingConversation || isSavingTitle) return;

    const titleToSave = titleDraft.trim();
    if (!titleToSave || titleToSave.length > 200) {
      setError("The title must contain between 1 and 200 characters.");
      return;
    }

    const original = editingConversation;
    setIsSavingTitle(true);
    setError(null);
    setConversations((current) => current.map((item) => item.id === original.id
      ? { ...item, title: titleToSave }
      : item));

    try {
      const saved = await updateConversationTitle(
        original.id,
        titleToSave,
        original.updatedAt
      );
      replaceConversation(saved);
      setEditingConversation(null);
      setTitleDraft("");
    } catch (saveError) {
      setConversations((current) => current.map((item) => item.id === original.id
        ? original
        : item));

      if (saveError instanceof ApiError && saveError.status === 409) {
        try {
          const latest = await getConversation(original.id);
          replaceConversation(latest);
          setEditingConversation(latest);
          setError(getConversationErrorMessage(saveError));
        } catch {
          setEditingConversation(null);
          setError("The conversation changed and its latest details could not be loaded. Reopen the title editor before trying again.");
        }
      } else {
        setError(getConversationErrorMessage(saveError));
      }
    } finally {
      setIsSavingTitle(false);
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
            maxLength={200}
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

      <form
        className="conversation-search"
        role="search"
        onSubmit={(event) => {
          event.preventDefault();
          handleSearch();
        }}
      >
        <label htmlFor="conversation-search-input">Search conversations</label>
        <input
          id="conversation-search-input"
          value={searchDraft}
          onChange={(event) => setSearchDraft(event.target.value)}
          maxLength={100}
          placeholder="Title or language"
          disabled={isLoading || isReloading || isLoadingMore}
        />

        <div className="conversation-search-dates">
          <label htmlFor="conversation-updated-from">Updated from</label>
          <input
            id="conversation-updated-from"
            type="date"
            value={updatedFromDraft}
            onChange={(event) => setUpdatedFromDraft(event.target.value)}
            disabled={isLoading || isReloading || isLoadingMore}
          />

          <label htmlFor="conversation-updated-to">Updated to</label>
          <input
            id="conversation-updated-to"
            type="date"
            value={updatedToDraft}
            onChange={(event) => setUpdatedToDraft(event.target.value)}
            disabled={isLoading || isReloading || isLoadingMore}
          />
        </div>

        <div className="conversation-search-actions">
          <button
            className="primary-button"
            type="submit"
            disabled={isLoading || isReloading || isLoadingMore}
          >
            Search
          </button>
          <button
            className="text-button"
            type="button"
            onClick={handleClearSearch}
            disabled={
              isLoading ||
              isReloading ||
              isLoadingMore ||
              !hasActiveFilters &&
              !searchDraft &&
              !updatedFromDraft &&
              !updatedToDraft
            }
          >
            Clear
          </button>
        </div>
      </form>

      <div className="sidebar-actions">
        <span className="conversation-count">
          {sortedConversations.length} conversation
          {sortedConversations.length === 1 ? "" : "s"}
        </span>
        <button
          className="text-button"
          type="button"
          onClick={() => void loadConversations(filters, true)}
          disabled={isReloading || isLoading || isLoadingMore}
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
            {hasActiveFilters
              ? "No conversations match your current filters."
              : "No conversations yet. Create one to get started."}
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
                  <span className="conversation-timestamps">
                    <time dateTime={conversation.createdAt}>Created {formatTimestamp(conversation.createdAt)}</time>
                    <time dateTime={conversation.updatedAt}>Updated {formatTimestamp(conversation.updatedAt)}</time>
                  </span>
                </button>
                <div className="conversation-item-actions">
                  <button
                    className="text-button"
                    type="button"
                    onClick={() => void handleStartRename(conversation)}
                    disabled={isLoadingTitleId !== null || isSavingTitle || deletingId === conversation.id}
                    aria-label={"Rename " + conversation.title}
                    title="Rename conversation"
                  >
                    {isLoadingTitleId === conversation.id ? "Loading…" : "Rename"}
                  </button>
                  <button
                    className="delete-button"
                    type="button"
                    onClick={() => void handleDelete(conversation)}
                    disabled={deletingId === conversation.id || isSavingTitle || isLoadingTitleId !== null}
                    aria-label={"Delete " + conversation.title}
                    title="Delete conversation"
                  >
                    {deletingId === conversation.id ? "…" : "Delete"}
                  </button>
                </div>
                {editingConversation?.id === conversation.id && (
                  <form className="conversation-rename-form" onSubmit={(event) => void handleSaveTitle(event)}>
                    <label htmlFor="conversation-rename-title">Conversation title</label>
                    <input
                      id="conversation-rename-title"
                      aria-label="Edit conversation title"
                      value={titleDraft}
                      onChange={(event) => setTitleDraft(event.target.value)}
                      maxLength={200}
                      disabled={isSavingTitle}
                    />
                    <div className="conversation-rename-actions">
                      <button
                        className="primary-button"
                        type="submit"
                        disabled={isSavingTitle || !titleDraft.trim() || titleDraft.trim() === editingConversation.title}
                      >
                        {isSavingTitle ? "Saving…" : "Save title"}
                      </button>
                      <button
                        className="text-button"
                        type="button"
                        onClick={() => setEditingConversation(null)}
                        disabled={isSavingTitle}
                      >
                        Cancel
                      </button>
                    </div>
                  </form>
                )}
              </div>
            ))}
            {nextCursor && (
              <button
                className="text-button"
                type="button"
                onClick={() => void loadMoreConversations()}
                disabled={isLoadingMore || isReloading}
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
