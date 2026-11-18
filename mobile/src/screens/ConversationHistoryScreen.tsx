import { useCallback, useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  StyleSheet,
  Text,
  TextInput,
  View
} from "react-native";
import {
  listConversations,
  type ConversationListFilters,
  type ConversationSummary
} from "../api/conversationService";
import { ApiError } from "../api/apiClient";

const PAGE_SIZE = 30;

interface ConversationHistoryScreenProps {
  onSelectConversation: (conversationId: string) => void;
  successNotice?: string | null;
  onDismissNotice?: () => void;
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Date unavailable";
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(date);
}

function parseDateFilter(
  value: string,
  endOfDayExclusive: boolean
): string | undefined {
  const normalized = value.trim();
  if (!normalized) {
    return undefined;
  }

  if (!/^\d{4}-\d{2}-\d{2}$/.test(normalized)) {
    throw new Error("Use the YYYY-MM-DD date format.");
  }

  const date = new Date(normalized + "T00:00:00.000Z");
  if (Number.isNaN(date.getTime()) || date.toISOString().slice(0, 10) !== normalized) {
    throw new Error("Enter a valid calendar date.");
  }

  if (endOfDayExclusive) {
    date.setUTCDate(date.getUTCDate() + 1);
  }

  return date.toISOString();
}

function getConversationLoadMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) {
      return "Your session has expired. Please sign in again.";
    }
    if (error.status === 0) {
      return "Conversation history could not be reached. Check your connection.";
    }
    if (error.status >= 500) {
      return "The conversation service is temporarily unavailable.";
    }
  }

  return error instanceof Error
    ? error.message
    : "Conversation history could not be loaded.";
}

function getEmptyMessage(filters: ConversationListFilters): string {
  return filters.search || filters.updatedFrom || filters.updatedTo
    ? "No conversations match these filters."
    : "Your saved conversations will appear here.";
}

export function ConversationHistoryScreen({
  onSelectConversation,
  successNotice = null,
  onDismissNotice
}: ConversationHistoryScreenProps) {
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [searchDraft, setSearchDraft] = useState("");
  const [updatedFromDraft, setUpdatedFromDraft] = useState("");
  const [updatedToDraft, setUpdatedToDraft] = useState("");
  const [filters, setFilters] = useState<ConversationListFilters>({});
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestRef = useRef<AbortController | null>(null);
  const loadMoreRequestRef = useRef<AbortController | null>(null);
  const loadMoreLockRef = useRef(false);
  const filtersRef = useRef<ConversationListFilters>({});

  const loadFirstPage = useCallback(async (
    nextFilters: ConversationListFilters,
    refresh = false
  ) => {
    requestRef.current?.abort();
    loadMoreRequestRef.current?.abort();
    loadMoreLockRef.current = false;
    setIsLoadingMore(false);

    const filtersChanged =
      filtersRef.current.search !== nextFilters.search ||
      filtersRef.current.updatedFrom !== nextFilters.updatedFrom ||
      filtersRef.current.updatedTo !== nextFilters.updatedTo;
    filtersRef.current = nextFilters;
    setFilters(nextFilters);

    if (filtersChanged) {
      setConversations([]);
      setNextCursor(null);
    }

    const controller = new AbortController();
    requestRef.current = controller;

    if (filtersChanged) {
      setIsLoading(true);
    } else if (refresh || conversations.length > 0) {
      setIsRefreshing(true);
    } else {
      setIsLoading(true);
    }

    setError(null);

    try {
      const page = await listConversations(
        undefined,
        controller.signal,
        nextFilters,
        PAGE_SIZE
      );

      if (!controller.signal.aborted) {
        setConversations(page.items);
        setNextCursor(page.nextCursor);
      }
    } catch (reason: unknown) {
      if (!controller.signal.aborted) {
        setError(getConversationLoadMessage(reason));
      }
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
        setIsRefreshing(false);
        requestRef.current = null;
      }
    }
  }, [conversations.length]);

  useEffect(() => {
    void loadFirstPage({});
    return () => {
      requestRef.current?.abort();
      loadMoreRequestRef.current?.abort();
    };
  }, []);

  const loadMore = useCallback(async () => {
    if (!nextCursor || isLoadingMore || isLoading || isRefreshing || loadMoreLockRef.current) {
      return;
    }

    loadMoreLockRef.current = true;
    const controller = new AbortController();
    loadMoreRequestRef.current = controller;
    setIsLoadingMore(true);
    setError(null);

    try {
      const page = await listConversations(
        nextCursor,
        controller.signal,
        filters,
        PAGE_SIZE
      );

      if (!controller.signal.aborted) {
        setConversations((current) => {
          const existingIds = new Set(current.map((item) => item.id));
          return [
            ...current,
            ...page.items.filter((item) => !existingIds.has(item.id))
          ];
        });
        setNextCursor(page.nextCursor);
      }
    } catch (reason: unknown) {
      if (!controller.signal.aborted) {
        setError(getConversationLoadMessage(reason));
      }
    } finally {
      if (!controller.signal.aborted) {
        setIsLoadingMore(false);
        loadMoreLockRef.current = false;
        loadMoreRequestRef.current = null;
      }
    }
  }, [filters, isLoading, isLoadingMore, isRefreshing, nextCursor]);

  function applyFilters() {
    try {
      const updatedFrom = parseDateFilter(updatedFromDraft, false);
      const updatedTo = parseDateFilter(updatedToDraft, true);

      if (updatedFrom && updatedToDraft.trim()) {
        const requestedEnd = parseDateFilter(updatedToDraft, false);
        if (requestedEnd && updatedFrom > requestedEnd) {
          setError("The start date must be earlier than or equal to the end date.");
          return;
        }
      }

      const nextFilters: ConversationListFilters = {
        search: searchDraft.trim() || undefined,
        updatedFrom,
        updatedTo
      };

      void loadFirstPage(nextFilters, true);
    } catch (reason: unknown) {
      setError(getConversationLoadMessage(reason));
    }
  }

  function clearFilters() {
    setSearchDraft("");
    setUpdatedFromDraft("");
    setUpdatedToDraft("");
    void loadFirstPage({}, true);
  }

  const hasFilters = Boolean(filters.search || filters.updatedFrom || filters.updatedTo);

  return (
    <View style={styles.screen}>
      <View style={styles.filterCard}>
        <Text style={styles.sectionTitle}>Find a conversation</Text>
        <TextInput
          value={searchDraft}
          onChangeText={setSearchDraft}
          onSubmitEditing={applyFilters}
          returnKeyType="search"
          placeholder="Search title or languages"
          accessibilityLabel="Search saved conversations"
          style={styles.searchInput}
          maxLength={100}
          autoCapitalize="none"
        />
        <View style={styles.dateRow}>
          <TextInput
            value={updatedFromDraft}
            onChangeText={setUpdatedFromDraft}
            placeholder="From (YYYY-MM-DD)"
            accessibilityLabel="Updated from date"
            style={[styles.searchInput, styles.dateInput]}
            keyboardType="numbers-and-punctuation"
            maxLength={10}
          />
          <TextInput
            value={updatedToDraft}
            onChangeText={setUpdatedToDraft}
            placeholder="To (YYYY-MM-DD)"
            accessibilityLabel="Updated to date"
            style={[styles.searchInput, styles.dateInput]}
            keyboardType="numbers-and-punctuation"
            maxLength={10}
          />
        </View>
        <View style={styles.filterActions}>
          <Pressable
            accessibilityRole="button"
            style={styles.primaryButton}
            onPress={applyFilters}
            disabled={isRefreshing}
          >
            <Text style={styles.primaryButtonText}>Apply filters</Text>
          </Pressable>
          <Pressable
            accessibilityRole="button"
            style={styles.textButton}
            onPress={clearFilters}
            disabled={isRefreshing || !searchDraft && !updatedFromDraft && !updatedToDraft && !hasFilters}
          >
            <Text style={styles.textButtonLabel}>Clear</Text>
          </Pressable>
          <Pressable
            accessibilityRole="button"
            style={styles.textButton}
            onPress={() => void loadFirstPage(filters, true)}
            disabled={isRefreshing || isLoading}
          >
            <Text style={styles.textButtonLabel}>Refresh</Text>
          </Pressable>
        </View>
      </View>

      {successNotice ? (
        <View style={styles.successCard}>
          <Text style={styles.successText}>{successNotice}</Text>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Dismiss notification"
            onPress={onDismissNotice}
          >
            <Text style={styles.successDismiss}>Dismiss</Text>
          </Pressable>
        </View>
      ) : null}

      {error ? (
        <View style={styles.errorCard}>
          <Text style={styles.errorText} accessibilityRole="alert">{error}</Text>
          <Pressable
            accessibilityRole="button"
            onPress={() => void loadFirstPage(filters, true)}
            style={styles.retryButton}
          >
            <Text style={styles.retryText}>Try again</Text>
          </Pressable>
        </View>
      ) : null}

      {isLoading && conversations.length === 0 ? (
        <View style={styles.stateCard}>
          <ActivityIndicator />
          <Text style={styles.stateText}>Loading saved conversations…</Text>
        </View>
      ) : (
        <FlatList
          data={conversations}
          keyExtractor={(item) => item.id}
          style={styles.list}
          contentContainerStyle={[
            styles.listContent,
            conversations.length === 0 && styles.emptyListContent
          ]}
          refreshControl={
            <RefreshControl
              refreshing={isRefreshing}
              onRefresh={() => void loadFirstPage(filters, true)}
            />
          }
          renderItem={({ item }) => (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={"Open conversation " + item.title}
              onPress={() => onSelectConversation(item.id)}
              style={({ pressed }) => [
                styles.conversationCard,
                pressed && styles.pressedCard
              ]}
            >
              <View style={styles.cardTopRow}>
                <Text numberOfLines={2} style={styles.conversationTitle}>
                  {item.title}
                </Text>
                <Text style={styles.updatedAt}>{formatTimestamp(item.updatedAt)}</Text>
              </View>
              <View style={styles.cardBottomRow}>
                <Text style={styles.languagePair}>
                  {item.sourceLanguage.toUpperCase()} → {item.targetLanguage.toUpperCase()}
                </Text>
                <Text style={styles.lifecycle}>{item.lifecycleState}</Text>
              </View>
            </Pressable>
          )}
          ListEmptyComponent={
            !isLoading ? (
              <View style={styles.stateCard}>
                <Text style={styles.emptyTitle}>
                  {hasFilters ? "No matching conversations" : "No saved conversations yet"}
                </Text>
                <Text style={styles.stateText}>{getEmptyMessage(filters)}</Text>
              </View>
            ) : null
          }
          ListFooterComponent={
            nextCursor ? (
              <Pressable
                accessibilityRole="button"
                style={[styles.loadMoreButton, isLoadingMore && styles.disabled]}
                onPress={() => void loadMore()}
                disabled={isLoadingMore}
              >
                {isLoadingMore
                  ? <ActivityIndicator />
                  : <Text style={styles.loadMoreText}>Load more conversations</Text>}
              </Pressable>
            ) : conversations.length > 0 ? (
              <Text style={styles.endOfList}>You’re all caught up.</Text>
            ) : null
          }
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, gap: 12 },
  filterCard: {
    gap: 10,
    padding: 14,
    borderRadius: 16,
    backgroundColor: "#ffffff"
  },
  sectionTitle: { color: "#101828", fontSize: 15, fontWeight: "800" },
  searchInput: {
    minHeight: 46,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    borderRadius: 11,
    paddingHorizontal: 12,
    color: "#101828",
    backgroundColor: "#ffffff"
  },
  dateRow: { flexDirection: "row", gap: 8 },
  dateInput: { flex: 1, minWidth: 0, fontSize: 12 },
  filterActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8
  },
  primaryButton: {
    minHeight: 42,
    paddingHorizontal: 14,
    borderRadius: 10,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  primaryButtonText: { color: "#ffffff", fontWeight: "800", fontSize: 12 },
  textButton: {
    minHeight: 42,
    minWidth: 48,
    paddingHorizontal: 8,
    alignItems: "center",
    justifyContent: "center"
  },
  textButtonLabel: { color: "#175cd3", fontWeight: "700", fontSize: 12 },
  list: { flex: 1 },
  listContent: { gap: 9, paddingBottom: 24 },
  emptyListContent: { flexGrow: 1, justifyContent: "center" },
  conversationCard: {
    gap: 10,
    padding: 14,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "#eaecf0",
    backgroundColor: "#ffffff"
  },
  pressedCard: { opacity: 0.7 },
  cardTopRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: 10
  },
  conversationTitle: {
    flex: 1,
    color: "#101828",
    fontSize: 15,
    fontWeight: "800",
    lineHeight: 20
  },
  updatedAt: { color: "#667085", fontSize: 10, maxWidth: 112, textAlign: "right" },
  cardBottomRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  languagePair: { color: "#475467", fontSize: 11, fontWeight: "700" },
  lifecycle: {
    color: "#475467",
    backgroundColor: "#f2f4f7",
    borderRadius: 999,
    overflow: "hidden",
    paddingHorizontal: 8,
    paddingVertical: 4,
    fontSize: 10,
    fontWeight: "700",
    textTransform: "capitalize"
  },
  stateCard: {
    alignItems: "center",
    justifyContent: "center",
    gap: 9,
    padding: 20,
    borderRadius: 14,
    backgroundColor: "#ffffff"
  },
  stateText: { color: "#667085", fontSize: 13, lineHeight: 19, textAlign: "center" },
  emptyTitle: { color: "#344054", fontSize: 16, fontWeight: "800" },
  loadMoreButton: {
    minHeight: 46,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#ffffff"
  },
  loadMoreText: { color: "#175cd3", fontWeight: "800", fontSize: 13 },
  endOfList: { color: "#98a2b3", fontSize: 11, textAlign: "center", padding: 10 },
  successCard: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
    padding: 11,
    borderRadius: 12,
    backgroundColor: "#ecfdf3"
  },
  successText: { flex: 1, color: "#067647", fontSize: 12, lineHeight: 18 },
  successDismiss: { color: "#067647", fontSize: 11, fontWeight: "800" },
  errorCard: {
    gap: 8,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#fef3f2"
  },
  errorText: { color: "#b42318", lineHeight: 18, fontSize: 12 },
  retryButton: { alignSelf: "flex-start", paddingVertical: 6 },
  retryText: { color: "#b42318", fontWeight: "800" },
  disabled: { opacity: 0.55 }
});
