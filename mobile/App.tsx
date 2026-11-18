import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  ActivityIndicator,
  Pressable,
  SafeAreaView,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  View
} from "react-native";
import {
  createConversation,
  listConversations,
  listMessages,
  type ConversationMessage,
  type ConversationSummary,
  type ConversationLifecycleState
} from "./src/api/conversationService";
import { processAssistantInput } from "./src/api/assistantService";
import { ApiError } from "./src/api/apiClient";
import { MicrophoneCapturePanel } from "./src/audio/MicrophoneCapturePanel";
import { ConversationHistoryScreen } from "./src/screens/ConversationHistoryScreen";
import { ConversationDetailScreen } from "./src/screens/ConversationDetailScreen";
import { subscribeToSessionExpired } from "./src/auth/authEvents";
import {
  getAuthErrorMessage,
  restoreSession,
  signIn,
  signOut,
  type AuthSession
} from "./src/auth/authService";

export default function App() {
  const [session, setSession] = useState<AuthSession | null>(null);
  const [activeScreen, setActiveScreen] = useState<"live" | "history" | "detail">("live");
  const [detailConversationId, setDetailConversationId] = useState<string | null>(null);
  const [checking, setChecking] = useState(true);
  const [authBusy, setAuthBusy] = useState(false);
  const [token, setToken] = useState("");
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [activeConversationId, setActiveConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [canRetryRestore, setCanRetryRestore] = useState(false);
  const [loadingConversations, setLoadingConversations] = useState(false);
  const [loadingHistory, setLoadingHistory] = useState(false);
  const [creatingConversation, setCreatingConversation] = useState(false);
  const [assistantBusy, setAssistantBusy] = useState(false);
  const [latestAiResult, setLatestAiResult] = useState<ConversationMessage | null>(null);
  const assistantBusyRef = useRef(false);
  const pendingTranscriptQueueRef = useRef<Array<{
    conversationId: string;
    text: string;
  }>>([]);
  const activeConversationIdRef = useRef<string | null>(activeConversationId);
  activeConversationIdRef.current = activeConversationId;

  const activeConversation = useMemo(
    () => conversations.find((item) => item.id === activeConversationId) ?? null,
    [activeConversationId, conversations]
  );

  const checkSavedSession = useCallback(async () => {
    setChecking(true);
    setError(null);
    setCanRetryRestore(false);

    try {
      setSession(await restoreSession());
    } catch (reason: unknown) {
      setSession(null);
      setError(getAuthErrorMessage(reason));
      setCanRetryRestore(true);
    } finally {
      setChecking(false);
    }
  }, []);

  useEffect(() => {
    void checkSavedSession();
  }, [checkSavedSession]);

  useEffect(() => subscribeToSessionExpired(() => {
    setSession(null);
    setActiveScreen("live");
    setDetailConversationId(null);
    setConversations([]);
    setActiveConversationId(null);
    setMessages([]);
    setLatestAiResult(null);
    setError("Your session has expired. Please sign in again.");
    setNotice(null);
    setCanRetryRestore(false);
  }), []);

  const loadConversations = useCallback(async () => {
    setLoadingConversations(true);
    try {
      const page = await listConversations();
      setConversations(page.items);
      setActiveConversationId((current) =>
        current && page.items.some((item) => item.id === current)
          ? current
          : page.items[0]?.id ?? null
      );
      setError(null);
    } catch (reason: unknown) {
      setError(
        reason instanceof ApiError
          ? reason.message
          : "Conversations could not be loaded. Check your connection and try again."
      );
    } finally {
      setLoadingConversations(false);
    }
  }, []);

  useEffect(() => {
    if (session) {
      void loadConversations();
    } else {
      setConversations([]);
      setActiveConversationId(null);
      setMessages([]);
    }
  }, [session, loadConversations]);

  useEffect(() => {
    // A transcript waiting for the previous conversation must never be sent
    // to a newly selected conversation.
    pendingTranscriptQueueRef.current = [];
  }, [activeConversationId]);

  useEffect(() => {
    if (!activeConversationId) {
      setMessages([]);
      setLatestAiResult(null);
      return;
    }

    let cancelled = false;
    setLoadingHistory(true);
    setLatestAiResult(null);

    void listMessages(activeConversationId)
      .then((page) => {
        if (!cancelled) {
          setMessages(page.items);
          setError(null);
        }
      })
      .catch((reason: unknown) => {
        if (!cancelled) {
          setError(
            reason instanceof ApiError
              ? reason.message
              : "Conversation history could not be loaded."
          );
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoadingHistory(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [activeConversationId]);

  async function handleSignIn() {
    setAuthBusy(true);
    setError(null);
    setNotice(null);

    try {
      const nextSession = await signIn(token);
      setSession(nextSession);
      setToken("");
      setCanRetryRestore(false);
    } catch (reason: unknown) {
      setError(getAuthErrorMessage(reason));
    } finally {
      setAuthBusy(false);
    }
  }

  async function handleSignOut() {
    setAuthBusy(true);
    try {
      const serverConfirmed = await signOut();
      setSession(null);
      setActiveScreen("live");
      setDetailConversationId(null);
      setConversations([]);
      setActiveConversationId(null);
      setMessages([]);
      setNotice(
        serverConfirmed
          ? "You have signed out successfully."
          : "You have signed out on this device, but the server could not confirm logout."
      );
      setError(null);
    } finally {
      setAuthBusy(false);
    }
  }

  async function handleCreateConversation() {
    const normalizedTitle = title.trim();
    if (!normalizedTitle || creatingConversation) {
      return;
    }

    setCreatingConversation(true);
    setError(null);

    try {
      const created = await createConversation(normalizedTitle);
      setConversations((current) => [created, ...current]);
      setActiveConversationId(created.id);
      setTitle("");
      setLatestAiResult(null);
      setMessages([]);
    } catch (reason: unknown) {
      setError(
        reason instanceof ApiError
          ? reason.message
          : "The conversation could not be created."
      );
    } finally {
      setCreatingConversation(false);
    }
  }

  async function handleFinalTranscript(text: string) {
    if (!activeConversation || !text.trim()) {
      return;
    }

    // Keep every final utterance in order and bind it to its originating conversation.
    if (pendingTranscriptQueueRef.current.length >= 50) {
      setError("The assistant is processing a backlog. Pause the microphone and retry when it catches up.");
      return;
    }

    pendingTranscriptQueueRef.current.push({
      conversationId: activeConversation.id,
      text: text.trim()
    });
    if (assistantBusyRef.current) {
      return;
    }

    assistantBusyRef.current = true;
    setAssistantBusy(true);
    setError(null);

    try {
      while (pendingTranscriptQueueRef.current.length > 0) {
        const nextItem = pendingTranscriptQueueRef.current.shift();
        if (!nextItem) {
          continue;
        }

        try {
          const result = await processAssistantInput(
            nextItem.conversationId,
            nextItem.text
          );
          const history = await listMessages(nextItem.conversationId);

          if (activeConversationIdRef.current === nextItem.conversationId) {
            setLatestAiResult(result);
            setMessages(history.items);
          }
        } catch (reason: unknown) {
          if (activeConversationIdRef.current === nextItem.conversationId) {
            setError(
              reason instanceof ApiError
                ? reason.message
                : "The AI could not process this speech. Try again."
            );
          }
        }
      }
    } finally {
      assistantBusyRef.current = false;
      setAssistantBusy(false);
    }
  }

  function handleLifecycleStateChange(lifecycleState: ConversationLifecycleState) {
    setConversations((current) =>
      current.map((item) =>
        item.id === activeConversationId
          ? { ...item, lifecycleState }
          : item
      )
    );
  }

  function openConversationDetails(conversationId: string) {
    setDetailConversationId(conversationId);
    setActiveScreen("detail");
  }

  function openLiveConversation(conversation: ConversationSummary) {
    setConversations((current) => {
      const exists = current.some((item) => item.id === conversation.id);
      return exists
        ? current.map((item) => item.id === conversation.id ? conversation : item)
        : [conversation, ...current];
    });
    setActiveConversationId(conversation.id);
    setDetailConversationId(null);
    setActiveScreen("live");
    setError(null);
  }

  function handleConversationDeleted(conversationId: string) {
    setConversations((current) => current.filter((item) => item.id !== conversationId));
    if (activeConversationId === conversationId) {
      setActiveConversationId(null);
      setMessages([]);
      setLatestAiResult(null);
    }
    setDetailConversationId(null);
    setActiveScreen("history");
    setNotice("The conversation was deleted.");
  }

  if (checking) {
    return (
      <SafeAreaView style={styles.center}>
        <ActivityIndicator />
        <Text style={styles.muted}>Checking saved session…</Text>
      </SafeAreaView>
    );
  }

  if (!session) {
    return (
      <SafeAreaView style={styles.container}>
        <View style={styles.loginCard}>
          <Text style={styles.eyebrow}>CONVERSA MOBILE</Text>
          <Text style={styles.title}>Sign in</Text>
          <Text style={styles.muted}>
            Enter an existing identity access token. Conversa exchanges it for a short-lived app token and stores credentials securely on this device.
          </Text>

          {notice ? <Text style={styles.notice}>{notice}</Text> : null}
          {error ? <Text accessibilityRole="alert" style={styles.error}>{error}</Text> : null}

          {canRetryRestore ? (
            <Pressable
              style={styles.secondaryButton}
              onPress={() => void checkSavedSession()}
              disabled={authBusy}
            >
              <Text style={styles.secondaryButtonText}>Retry saved session</Text>
            </Pressable>
          ) : null}

          <TextInput
            value={token}
            onChangeText={setToken}
            placeholder="Identity access token"
            autoCapitalize="none"
            autoCorrect={false}
            secureTextEntry
            editable={!authBusy}
            accessibilityLabel="Identity access token"
            style={styles.input}
          />

          <Pressable
            style={[styles.primaryButton, (!token.trim() || authBusy) && styles.disabled]}
            onPress={() => void handleSignIn()}
            disabled={!token.trim() || authBusy}
          >
            {authBusy
              ? <ActivityIndicator color="#fff" />
              : <Text style={styles.primaryButtonText}>Continue</Text>}
          </Pressable>
        </View>
      </SafeAreaView>
    );
  }

  if (activeScreen === "history") {
    return (
      <SafeAreaView style={styles.container}>
        <View style={styles.secondaryContent}>
          <View style={styles.topBar}>
            <View>
              <Text style={styles.eyebrow}>CONVERSA MOBILE</Text>
              <Text style={styles.title}>History</Text>
            </View>
            <View style={styles.headerActions}>
              <Pressable
                style={styles.historyButton}
                onPress={() => setActiveScreen("live")}
              >
                <Text style={styles.historyButtonText}>Live</Text>
              </Pressable>
              <Pressable
                style={styles.signOutButton}
                disabled={authBusy}
                onPress={() => void handleSignOut()}
              >
                <Text style={styles.signOutText}>{authBusy ? "Signing out…" : "Sign out"}</Text>
              </Pressable>
            </View>
          </View>

          <ConversationHistoryScreen
            onSelectConversation={openConversationDetails}
            successNotice={notice}
            onDismissNotice={() => setNotice(null)}
          />
        </View>
      </SafeAreaView>
    );
  }

  if (activeScreen === "detail" && detailConversationId) {
    return (
      <SafeAreaView style={styles.container}>
        <View style={styles.secondaryContent}>
          <View style={styles.topBar}>
            <View>
              <Text style={styles.eyebrow}>CONVERSATION DETAILS</Text>
              <Text style={styles.title}>Saved conversation</Text>
            </View>
            <View style={styles.headerActions}>
              <Pressable
                style={styles.historyButton}
                onPress={() => setActiveScreen("history")}
              >
                <Text style={styles.historyButtonText}>History</Text>
              </Pressable>
              <Pressable
                style={styles.signOutButton}
                disabled={authBusy}
                onPress={() => void handleSignOut()}
              >
                <Text style={styles.signOutText}>{authBusy ? "Signing out…" : "Sign out"}</Text>
              </Pressable>
            </View>
          </View>

          <ConversationDetailScreen
            conversationId={detailConversationId}
            onOpenLive={openLiveConversation}
            onDeleted={handleConversationDeleted}
            onBackToHistory={() => setActiveScreen("history")}
          />
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container}>
      <ScrollView
        contentContainerStyle={styles.content}
        keyboardShouldPersistTaps="handled"
      >
        <View style={styles.topBar}>
          <View>
            <Text style={styles.eyebrow}>REAL-TIME CONVERSATION ASSISTANT</Text>
            <Text style={styles.title}>Conversa</Text>
          </View>
          <View style={styles.headerActions}>
            <Pressable
              style={styles.historyButton}
              onPress={() => setActiveScreen("history")}
            >
              <Text style={styles.historyButtonText}>History</Text>
            </Pressable>
            <Pressable
              style={styles.signOutButton}
              disabled={authBusy}
              onPress={() => void handleSignOut()}
            >
              <Text style={styles.signOutText}>{authBusy ? "Signing out…" : "Sign out"}</Text>
            </Pressable>
          </View>
        </View>

        <View style={styles.createCard}>
          <Text style={styles.sectionTitle}>New conversation</Text>
          <View style={styles.createRow}>
            <TextInput
              value={title}
              onChangeText={setTitle}
              placeholder="e.g. Job interview"
              accessibilityLabel="New conversation title"
              style={[styles.input, styles.flex]}
            />
            <Pressable
              style={[styles.createButton, (!title.trim() || creatingConversation) && styles.disabled]}
              disabled={!title.trim() || creatingConversation}
              onPress={() => void handleCreateConversation()}
            >
              {creatingConversation
                ? <ActivityIndicator color="#fff" />
                : <Text style={styles.primaryButtonText}>Create</Text>}
            </Pressable>
          </View>
        </View>

        <View style={styles.sectionHeader}>
          <Text style={styles.sectionTitle}>Your conversations</Text>
          <Pressable onPress={() => void loadConversations()} disabled={loadingConversations}>
            <Text style={styles.linkText}>{loadingConversations ? "Refreshing…" : "Refresh"}</Text>
          </Pressable>
        </View>

        {loadingConversations && conversations.length === 0 ? (
          <ActivityIndicator />
        ) : conversations.length === 0 ? (
          <View style={styles.emptyCard}>
            <Text style={styles.muted}>No conversations yet. Create one to begin.</Text>
          </View>
        ) : (
          <ScrollView
            horizontal
            showsHorizontalScrollIndicator={false}
            contentContainerStyle={styles.conversationList}
          >
            {conversations.map((conversation) => (
              <Pressable
                key={conversation.id}
                accessibilityRole="button"
                accessibilityState={{ selected: activeConversationId === conversation.id }}
                onPress={() => setActiveConversationId(conversation.id)}
                style={[
                  styles.conversationChip,
                  activeConversationId === conversation.id && styles.conversationChipActive
                ]}
              >
                <Text
                  numberOfLines={1}
                  style={[
                    styles.conversationChipTitle,
                    activeConversationId === conversation.id && styles.conversationChipTitleActive
                  ]}
                >
                  {conversation.title}
                </Text>
                <Text style={styles.conversationChipMeta}>
                  {conversation.sourceLanguage.toUpperCase()} → {conversation.targetLanguage.toUpperCase()}
                </Text>
                <Text style={styles.conversationChipMeta}>
                  {conversation.lifecycleState}
                </Text>
              </Pressable>
            ))}
          </ScrollView>
        )}

        {error ? <Text accessibilityRole="alert" style={styles.error}>{error}</Text> : null}

        {activeConversation ? (
          <>
            <View style={styles.activeHeader}>
              <Text style={styles.sectionKicker}>ACTIVE CONVERSATION</Text>
              <Text style={styles.conversationTitle}>{activeConversation.title}</Text>
              <Text style={styles.muted}>
                {activeConversation.sourceLanguage.toUpperCase()} → {activeConversation.targetLanguage.toUpperCase()}
              </Text>
            </View>

            <MicrophoneCapturePanel
              conversationId={activeConversation.id}
              lifecycleState={activeConversation.lifecycleState}
              onLifecycleStateChange={handleLifecycleStateChange}
              onFinalTranscript={handleFinalTranscript}
            />

            {assistantBusy ? (
              <View style={styles.aiLoading}>
                <ActivityIndicator />
                <Text style={styles.muted}>Conversa is translating and preparing assistance…</Text>
              </View>
            ) : null}

            {latestAiResult ? (
              <AiResultCard message={latestAiResult} />
            ) : null}

            <View style={styles.historyCard}>
              <View style={styles.sectionHeader}>
                <Text style={styles.sectionTitle}>Conversation history</Text>
                {loadingHistory ? <ActivityIndicator size="small" /> : null}
              </View>

              {messages.length === 0 && !loadingHistory ? (
                <Text style={styles.muted}>
                  No messages yet. Start the microphone and speak to see the conversation here.
                </Text>
              ) : (
                [...messages].reverse().map((message) => (
                  <View key={message.id} style={styles.messageItem}>
                    <Text style={styles.messageRole}>
                      {message.role === "speaker"
                        ? "SPEAKER"
                        : message.role === "user"
                          ? "YOU"
                          : message.role.toUpperCase()}
                    </Text>
                    <Text style={styles.messageOriginal}>{message.originalText}</Text>
                    {message.translation ? (
                      <Text style={styles.messageTranslation}>{message.translation}</Text>
                    ) : null}
                    {message.explanation ? (
                      <Text style={styles.messageExplanation}>{message.explanation}</Text>
                    ) : null}
                    {message.suggestedAnswer ? (
                      <View style={styles.answerBox}>
                        <Text style={styles.answerLabel}>SUGGESTED REPLY</Text>
                        <Text style={styles.messageTranslation}>{message.suggestedAnswer}</Text>
                        {message.suggestedAnswerTranslation ? (
                          <Text style={styles.messageExplanation}>
                            {message.suggestedAnswerTranslation}
                          </Text>
                        ) : null}
                      </View>
                    ) : null}
                  </View>
                ))
              )}
            </View>
          </>
        ) : (
          <View style={styles.emptyCard}>
            <Text style={styles.sectionTitle}>Select a conversation</Text>
            <Text style={styles.muted}>
              Choose a conversation above or create a new one to start a live session.
            </Text>
          </View>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function AiResultCard({ message }: { message: ConversationMessage }) {
  return (
    <View style={styles.aiCard}>
      <Text style={styles.sectionKicker}>CONVERSA ASSISTANT</Text>
      <Text style={styles.messageOriginal}>{message.originalText}</Text>
      {message.translation ? (
        <Text style={styles.aiTranslation}>{message.translation}</Text>
      ) : null}
      {message.explanation ? (
        <Text style={styles.messageExplanation}>{message.explanation}</Text>
      ) : null}
      {message.suggestedAnswer ? (
        <View style={styles.answerBox}>
          <Text style={styles.answerLabel}>SUGGESTED REPLY</Text>
          <Text style={styles.aiTranslation}>{message.suggestedAnswer}</Text>
          {message.suggestedAnswerTranslation ? (
            <Text style={styles.messageExplanation}>{message.suggestedAnswerTranslation}</Text>
          ) : null}
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#f5f7fb",
    paddingHorizontal: 16
  },
  content: {
    gap: 14,
    paddingTop: 14,
    paddingBottom: 36
  },
  secondaryContent: {
    flex: 1,
    gap: 14,
    paddingTop: 14,
    paddingBottom: 20
  },
  headerActions: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8
  },
  historyButton: {
    minHeight: 44,
    minWidth: 62,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: "#b2ddff",
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#eff8ff"
  },
  historyButtonText: {
    color: "#175cd3",
    fontWeight: "800",
    fontSize: 12
  },
  center: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 10
  },
  loginCard: {
    gap: 14,
    margin: 16,
    padding: 20,
    borderRadius: 20,
    backgroundColor: "#ffffff"
  },
  topBar: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 10
  },
  eyebrow: {
    color: "#667085",
    fontSize: 10,
    fontWeight: "800",
    letterSpacing: 1.3
  },
  title: {
    color: "#101828",
    fontSize: 28,
    fontWeight: "800"
  },
  muted: {
    color: "#667085",
    lineHeight: 19
  },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    borderRadius: 12,
    paddingHorizontal: 14,
    backgroundColor: "#ffffff"
  },
  createCard: {
    gap: 10,
    padding: 14,
    borderRadius: 16,
    backgroundColor: "#ffffff"
  },
  createRow: {
    flexDirection: "row",
    gap: 8,
    alignItems: "center"
  },
  flex: { flex: 1 },
  createButton: {
    minHeight: 48,
    minWidth: 76,
    paddingHorizontal: 14,
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  primaryButton: {
    minHeight: 48,
    borderRadius: 12,
    paddingHorizontal: 16,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  primaryButtonText: {
    color: "#ffffff",
    fontWeight: "800"
  },
  secondaryButton: {
    minHeight: 44,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    borderRadius: 12,
    alignItems: "center",
    justifyContent: "center"
  },
  secondaryButtonText: { color: "#344054", fontWeight: "700" },
  disabled: { opacity: 0.5 },
  signOutButton: {
    minHeight: 44,
    minWidth: 76,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    alignItems: "center",
    justifyContent: "center"
  },
  signOutText: { color: "#344054", fontWeight: "700", fontSize: 12 },
  sectionHeader: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between"
  },
  sectionTitle: { color: "#101828", fontSize: 16, fontWeight: "800" },
  sectionKicker: { color: "#667085", fontSize: 10, fontWeight: "800", letterSpacing: 1.2 },
  linkText: { color: "#175cd3", fontWeight: "700", minHeight: 32, textAlignVertical: "center" },
  conversationList: { gap: 9, paddingRight: 8 },
  conversationChip: {
    width: 160,
    minHeight: 84,
    padding: 12,
    borderRadius: 14,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    backgroundColor: "#ffffff",
    justifyContent: "center",
    gap: 5
  },
  conversationChipActive: {
    borderColor: "#175cd3",
    backgroundColor: "#eff6ff"
  },
  conversationChipTitle: { color: "#344054", fontWeight: "700" },
  conversationChipTitleActive: { color: "#1849a9" },
  conversationChipMeta: { color: "#667085", fontSize: 10 },
  activeHeader: {
    gap: 4,
    paddingTop: 3
  },
  conversationTitle: { color: "#101828", fontSize: 22, fontWeight: "800" },
  emptyCard: {
    gap: 8,
    padding: 18,
    borderRadius: 16,
    backgroundColor: "#ffffff"
  },
  historyCard: {
    gap: 12,
    padding: 15,
    borderRadius: 16,
    backgroundColor: "#ffffff"
  },
  messageItem: {
    gap: 7,
    paddingVertical: 11,
    borderTopWidth: 1,
    borderTopColor: "#eaecf0"
  },
  messageRole: { color: "#667085", fontSize: 10, fontWeight: "800", letterSpacing: 1 },
  messageOriginal: { color: "#101828", fontSize: 15, lineHeight: 21 },
  messageTranslation: { color: "#175cd3", fontSize: 15, lineHeight: 21, fontWeight: "600" },
  messageExplanation: { color: "#475467", fontSize: 13, lineHeight: 19 },
  aiCard: {
    gap: 10,
    padding: 16,
    borderRadius: 16,
    borderWidth: 1,
    borderColor: "#b2ddff",
    backgroundColor: "#eff8ff"
  },
  aiTranslation: { color: "#1849a9", fontSize: 18, lineHeight: 25, fontWeight: "700" },
  answerBox: {
    gap: 6,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#ffffff"
  },
  answerLabel: { color: "#667085", fontSize: 10, fontWeight: "800", letterSpacing: 1 },
  aiLoading: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    padding: 12,
    borderRadius: 12,
    backgroundColor: "#ffffff"
  },
  error: { color: "#b42318", lineHeight: 19 },
  notice: { color: "#175cd3" }
});
