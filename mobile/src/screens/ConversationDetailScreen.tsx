import { useCallback, useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  FlatList,
  Pressable,
  StyleSheet,
  Text,
  View
} from "react-native";
import {
  deleteConversation,
  getConversation,
  listMessages,
  type ConversationMessage,
  type ConversationSummary
} from "../api/conversationService";
import { ApiError } from "../api/apiClient";

const PAGE_SIZE = 30;

interface ConversationDetailScreenProps {
  conversationId: string;
  onOpenLive: (conversation: ConversationSummary) => void;
  onDeleted: (conversationId: string) => void;
  onBackToHistory: () => void;
}

function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Date unavailable";

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(date);
}

function getDetailErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) {
      return "This conversation is no longer available. It may have been deleted.";
    }
    if (error.status === 401) {
      return "Your session has expired. Please sign in again.";
    }
    if (error.status === 0) {
      return "The conversation could not be reached. Check your connection.";
    }
    if (error.status >= 500) {
      return "The conversation service is temporarily unavailable.";
    }
  }

  return "The conversation details could not be loaded.";
}

function roleLabel(role: ConversationMessage["role"]): string {
  switch (role) {
    case "speaker":
      return "SPEAKER";
    case "user":
      return "YOU";
    case "assistant":
      return "CONVERSA";
    default:
      return "NOTE";
  }
}

export function ConversationDetailScreen({
  conversationId,
  onOpenLive,
  onDeleted,
  onBackToHistory
}: ConversationDetailScreenProps) {
  const [conversation, setConversation] = useState<ConversationSummary | null>(null);
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingOlder, setIsLoadingOlder] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notFound, setNotFound] = useState(false);
  const requestRef = useRef<AbortController | null>(null);
  const olderRequestRef = useRef<AbortController | null>(null);
  const olderLockRef = useRef(false);

  const loadDetail = useCallback(async () => {
    requestRef.current?.abort();
    olderRequestRef.current?.abort();
    olderLockRef.current = false;
    setIsLoadingOlder(false);
    setConversation(null);
    setMessages([]);
    setNextCursor(null);
    const controller = new AbortController();
    requestRef.current = controller;

    setIsLoading(true);
    setError(null);
    setNotFound(false);

    try {
      const [summary, page] = await Promise.all([
        getConversation(conversationId, controller.signal),
        listMessages(conversationId, undefined, controller.signal, PAGE_SIZE)
      ]);

      if (!controller.signal.aborted) {
        setConversation(summary);
        setMessages(page.items);
        setNextCursor(page.nextCursor);
      }
    } catch (reason: unknown) {
      if (!controller.signal.aborted) {
        setError(getDetailErrorMessage(reason));
        setNotFound(reason instanceof ApiError && reason.status === 404);
      }
    } finally {
      if (!controller.signal.aborted) {
        setIsLoading(false);
        requestRef.current = null;
      }
    }
  }, [conversationId]);

  useEffect(() => {
    void loadDetail();
    return () => {
      requestRef.current?.abort();
      olderRequestRef.current?.abort();
    };
  }, [loadDetail]);

  const loadOlderMessages = useCallback(async () => {
    if (!nextCursor || isLoadingOlder || isLoading || olderLockRef.current) {
      return;
    }

    olderLockRef.current = true;
    const controller = new AbortController();
    olderRequestRef.current = controller;
    setIsLoadingOlder(true);
    setError(null);

    try {
      const page = await listMessages(
        conversationId,
        nextCursor,
        controller.signal,
        PAGE_SIZE
      );

      if (!controller.signal.aborted) {
        setMessages((current) => {
          const existingIds = new Set(current.map((item) => item.id));
          const older = page.items.filter((item) => !existingIds.has(item.id));
          // The API returns each page oldest-to-newest. Older pages belong before
          // the currently loaded timeline.
          return [...older, ...current];
        });
        setNextCursor(page.nextCursor);
      }
    } catch (reason: unknown) {
      if (!controller.signal.aborted) {
        setError(getDetailErrorMessage(reason));
      }
    } finally {
      if (!controller.signal.aborted) {
        setIsLoadingOlder(false);
        olderLockRef.current = false;
        olderRequestRef.current = null;
      }
    }
  }, [conversationId, isLoading, isLoadingOlder, nextCursor]);

  function askToDelete() {
    Alert.alert(
      "Delete conversation?",
      "This permanently deletes the saved conversation and its message history.",
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Delete",
          style: "destructive",
          onPress: () => void confirmDelete()
        }
      ]
    );
  }

  async function confirmDelete() {
    if (isDeleting) return;

    setIsDeleting(true);
    setError(null);

    try {
      await deleteConversation(conversationId);
      onDeleted(conversationId);
    } catch (reason: unknown) {
      if (reason instanceof ApiError && reason.status === 404) {
        // The conversation may already have been deleted from another client.
        onDeleted(conversationId);
        return;
      }

      setError(getDetailErrorMessage(reason));
    } finally {
      setIsDeleting(false);
    }
  }

  if (isLoading && !conversation) {
    return (
      <View style={styles.stateCard}>
        <ActivityIndicator />
        <Text style={styles.stateText}>Loading conversation…</Text>
      </View>
    );
  }

  if (!conversation) {
    return (
      <View style={styles.stateCard}>
        <Text style={styles.stateTitle}>
          {notFound ? "Conversation unavailable" : "Could not open conversation"}
        </Text>
        <Text style={styles.stateText}>
          {error ?? "The conversation could not be loaded."}
        </Text>
        <View style={styles.actions}>
          <Pressable style={styles.secondaryButton} onPress={onBackToHistory}>
            <Text style={styles.secondaryText}>Back to history</Text>
          </Pressable>
          {!notFound ? (
            <Pressable style={styles.primaryButton} onPress={() => void loadDetail()}>
              <Text style={styles.primaryText}>Try again</Text>
            </Pressable>
          ) : null}
        </View>
      </View>
    );
  }

  return (
    <View style={styles.screen}>
      <View style={styles.summaryCard}>
        <Text style={styles.eyebrow}>SAVED CONVERSATION</Text>
        <Text style={styles.title}>{conversation.title}</Text>
        <View style={styles.metaRow}>
          <Text style={styles.languagePair}>
            {conversation.sourceLanguage.toUpperCase()} → {conversation.targetLanguage.toUpperCase()}
          </Text>
          <Text style={styles.lifecycle}>{conversation.lifecycleState}</Text>
        </View>
        <Text style={styles.updatedAt}>
          Last updated {formatTimestamp(conversation.updatedAt)}
        </Text>
        <View style={styles.actions}>
          <Pressable
            accessibilityRole="button"
            style={styles.primaryButton}
            onPress={() => onOpenLive(conversation)}
          >
            <Text style={styles.primaryText}>Open live conversation</Text>
          </Pressable>
          <Pressable
            accessibilityRole="button"
            style={[styles.deleteButton, isDeleting && styles.disabled]}
            disabled={isDeleting}
            onPress={askToDelete}
          >
            {isDeleting
              ? <ActivityIndicator color="#b42318" />
              : <Text style={styles.deleteText}>Delete</Text>}
          </Pressable>
        </View>
      </View>

      {error ? (
        <View style={styles.errorCard}>
          <Text style={styles.errorText} accessibilityRole="alert">{error}</Text>
          <Pressable onPress={() => void loadDetail()} style={styles.retryButton}>
            <Text style={styles.retryText}>Refresh details</Text>
          </Pressable>
        </View>
      ) : null}

      <View style={styles.messagesHeader}>
        <Text style={styles.sectionTitle}>Messages</Text>
        <Text style={styles.messageCount}>{messages.length} loaded</Text>
      </View>

      <FlatList
        data={messages}
        keyExtractor={(item) => item.id}
        style={styles.messageList}
        contentContainerStyle={[
          styles.messageListContent,
          messages.length === 0 && styles.emptyList
        ]}
        maintainVisibleContentPosition={{ minIndexForVisible: 0 }}
        ListHeaderComponent={
          nextCursor ? (
            <Pressable
              accessibilityRole="button"
              style={[styles.loadOlderButton, isLoadingOlder && styles.disabled]}
              onPress={() => void loadOlderMessages()}
              disabled={isLoadingOlder}
            >
              {isLoadingOlder
                ? <ActivityIndicator />
                : <Text style={styles.loadOlderText}>Load older messages</Text>}
            </Pressable>
          ) : messages.length > 0 ? (
            <Text style={styles.beginningLabel}>Beginning of conversation</Text>
          ) : null
        }
        renderItem={({ item }) => (
          <View style={styles.messageCard}>
            <View style={styles.messageHeader}>
              <Text style={styles.role}>{roleLabel(item.role)}</Text>
              <Text style={styles.messageDate}>{formatTimestamp(item.createdAt)}</Text>
            </View>
            <Text style={styles.originalText}>{item.originalText}</Text>
            {item.translation ? (
              <Text style={styles.translation}>{item.translation}</Text>
            ) : null}
            {item.explanation ? (
              <Text style={styles.explanation}>{item.explanation}</Text>
            ) : null}
            {item.suggestedAnswer ? (
              <View style={styles.answerBox}>
                <Text style={styles.answerLabel}>SUGGESTED REPLY</Text>
                <Text style={styles.translation}>{item.suggestedAnswer}</Text>
                {item.suggestedAnswerTranslation ? (
                  <Text style={styles.explanation}>{item.suggestedAnswerTranslation}</Text>
                ) : null}
              </View>
            ) : null}
          </View>
        )}
        ListEmptyComponent={
          <View style={styles.stateCard}>
            <Text style={styles.stateTitle}>No messages yet</Text>
            <Text style={styles.stateText}>
              This saved conversation does not contain any messages.
            </Text>
          </View>
        }
        showsVerticalScrollIndicator={false}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, gap: 12 },
  summaryCard: {
    gap: 9,
    padding: 15,
    borderRadius: 16,
    backgroundColor: "#ffffff"
  },
  eyebrow: { color: "#667085", fontSize: 10, fontWeight: "800", letterSpacing: 1.2 },
  title: { color: "#101828", fontSize: 22, fontWeight: "800" },
  metaRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  languagePair: { color: "#475467", fontSize: 12, fontWeight: "700" },
  lifecycle: {
    overflow: "hidden",
    paddingHorizontal: 9,
    paddingVertical: 5,
    borderRadius: 999,
    color: "#475467",
    backgroundColor: "#f2f4f7",
    fontSize: 10,
    fontWeight: "700",
    textTransform: "capitalize"
  },
  updatedAt: { color: "#667085", fontSize: 11 },
  actions: { flexDirection: "row", gap: 8, alignItems: "center" },
  primaryButton: {
    flex: 1,
    minHeight: 44,
    paddingHorizontal: 12,
    borderRadius: 10,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  primaryText: { color: "#ffffff", fontSize: 12, fontWeight: "800" },
  secondaryButton: {
    minHeight: 44,
    paddingHorizontal: 14,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    justifyContent: "center"
  },
  secondaryText: { color: "#344054", fontWeight: "700" },
  deleteButton: {
    minHeight: 44,
    minWidth: 76,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: "#fecdca",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#ffffff"
  },
  deleteText: { color: "#b42318", fontSize: 12, fontWeight: "800" },
  messagesHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  sectionTitle: { color: "#101828", fontSize: 16, fontWeight: "800" },
  messageCount: { color: "#667085", fontSize: 11 },
  messageList: { flex: 1 },
  messageListContent: { gap: 9, paddingBottom: 24 },
  messageCard: {
    gap: 8,
    padding: 13,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "#eaecf0",
    backgroundColor: "#ffffff"
  },
  messageHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8
  },
  role: { color: "#667085", fontSize: 10, fontWeight: "800", letterSpacing: 1 },
  messageDate: { color: "#98a2b3", fontSize: 10 },
  originalText: { color: "#101828", fontSize: 15, lineHeight: 21 },
  translation: { color: "#175cd3", fontSize: 15, lineHeight: 21, fontWeight: "700" },
  explanation: { color: "#475467", fontSize: 13, lineHeight: 19 },
  answerBox: { gap: 6, padding: 11, borderRadius: 11, backgroundColor: "#f8fafc" },
  answerLabel: { color: "#667085", fontSize: 10, fontWeight: "800", letterSpacing: 1 },
  beginningLabel: { color: "#98a2b3", fontSize: 11, textAlign: "center", paddingVertical: 6 },
  loadOlderButton: {
    minHeight: 44,
    borderRadius: 11,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#ffffff"
  },
  loadOlderText: { color: "#175cd3", fontWeight: "800", fontSize: 12 },
  emptyList: { flexGrow: 1, justifyContent: "center" },
  stateCard: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
    padding: 20,
    borderRadius: 14,
    backgroundColor: "#ffffff"
  },
  stateTitle: { color: "#344054", fontSize: 16, fontWeight: "800", textAlign: "center" },
  stateText: { color: "#667085", fontSize: 13, lineHeight: 19, textAlign: "center" },
  errorCard: { gap: 7, padding: 12, borderRadius: 12, backgroundColor: "#fef3f2" },
  errorText: { color: "#b42318", fontSize: 12, lineHeight: 18 },
  retryButton: { alignSelf: "flex-start", paddingVertical: 5 },
  retryText: { color: "#b42318", fontWeight: "800" },
  disabled: { opacity: 0.5 }
});
