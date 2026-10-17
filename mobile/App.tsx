import { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Pressable,
  SafeAreaView,
  StyleSheet,
  Text,
  TextInput,
  View
} from "react-native";
import {
  createConversation,
  listConversations,
  type ConversationSummary
} from "./src/api/conversationService";
import { ApiError } from "./src/api/apiClient";
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
  const [checking, setChecking] = useState(true);
  const [authBusy, setAuthBusy] = useState(false);
  const [token, setToken] = useState("");
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [canRetryRestore, setCanRetryRestore] = useState(false);

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
    setConversations([]);
    setError("Your session has expired. Please sign in again.");
    setNotice(null);
    setCanRetryRestore(false);
  }), []);

  useEffect(() => {
    if (!session) {
      setConversations([]);
      return;
    }

    let cancelled = false;
    void listConversations()
      .then((page) => {
        if (!cancelled) {
          setConversations(page.items);
          setError(null);
        }
      })
      .catch((reason: unknown) => {
        if (cancelled) return;
        setError(
          reason instanceof ApiError
            ? reason.message
            : "Conversations could not be loaded."
        );
      });

    return () => {
      cancelled = true;
    };
  }, [session]);

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
        <View style={styles.card}>
          <Text style={styles.eyebrow}>CONVERSA MOBILE</Text>
          <Text style={styles.title}>Sign in</Text>
          <Text style={styles.muted}>
            Enter an existing identity access token. It is exchanged for a
            short-lived Conversa token; credentials are stored in the device
            secure store.
          </Text>

          {notice ? <Text style={styles.notice}>{notice}</Text> : null}
          {error ? <Text accessibilityRole="alert" style={styles.error}>{error}</Text> : null}

          {canRetryRestore ? (
            <Pressable
              style={styles.secondaryButton}
              onPress={() => void checkSavedSession()}
              disabled={authBusy}
            >
              <Text>Retry saved session</Text>
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
            style={[styles.button, authBusy && styles.disabledButton]}
            onPress={() => {
              setError(null);
              setNotice(null);
              setAuthBusy(true);
              void signIn(token)
                .then((nextSession) => {
                  setSession(nextSession);
                  setToken("");
                  setCanRetryRestore(false);
                })
                .catch((reason: unknown) => setError(getAuthErrorMessage(reason)))
                .finally(() => setAuthBusy(false));
            }}
            disabled={!token.trim() || authBusy}
          >
            {authBusy ? (
              <ActivityIndicator color="#fff" />
            ) : (
              <Text style={styles.buttonText}>Continue</Text>
            )}
          </Pressable>
        </View>
      </SafeAreaView>
    );
  }

  return (
    <SafeAreaView style={styles.container}>
      <View style={styles.card}>
        <Text style={styles.eyebrow}>CONVERSA MOBILE</Text>
        <Text style={styles.title}>Your conversations</Text>
        <Text style={styles.muted}>Signed in</Text>

        <View style={styles.row}>
          <TextInput
            value={title}
            onChangeText={setTitle}
            placeholder="New conversation"
            accessibilityLabel="New conversation title"
            style={[styles.input, styles.flex]}
          />
          <Pressable
            style={styles.button}
            onPress={() => {
              void createConversation(title.trim())
                .then((conversation) => {
                  setConversations((current) => [conversation, ...current]);
                  setTitle("");
                  setError(null);
                })
                .catch((reason: unknown) =>
                  setError(
                    reason instanceof ApiError
                      ? reason.message
                      : "Conversation could not be created."
                  )
                );
            }}
            disabled={!title.trim()}
          >
            <Text style={styles.buttonText}>Create</Text>
          </Pressable>
        </View>

        {error ? <Text accessibilityRole="alert" style={styles.error}>{error}</Text> : null}

        {conversations.map((conversation) => (
          <View key={conversation.id} style={styles.conversation}>
            <Text style={styles.conversationTitle}>{conversation.title}</Text>
            <Text style={styles.muted}>
              {conversation.sourceLanguage.toUpperCase()} →{" "}
              {conversation.targetLanguage.toUpperCase()}
            </Text>
          </View>
        ))}

        <Pressable
          style={[styles.secondaryButton, authBusy && styles.disabledButton]}
          disabled={authBusy}
          onPress={() => {
            setAuthBusy(true);
            void signOut()
              .then((serverConfirmed) => {
                setSession(null);
                setConversations([]);
                setError(null);
                setNotice(
                  serverConfirmed
                    ? "You have signed out successfully."
                    : "You have signed out on this device, but the server could not confirm logout."
                );
              })
              .finally(() => setAuthBusy(false));
          }}
        >
          <Text>{authBusy ? "Signing out…" : "Sign out"}</Text>
        </Pressable>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: "#f5f7fb",
    padding: 20
  },
  center: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    gap: 10
  },
  card: {
    gap: 14,
    backgroundColor: "#ffffff",
    borderRadius: 20,
    padding: 20
  },
  eyebrow: {
    fontSize: 12,
    fontWeight: "700",
    letterSpacing: 1.5
  },
  title: {
    fontSize: 28,
    fontWeight: "700"
  },
  muted: {
    color: "#667085"
  },
  input: {
    minHeight: 48,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    borderRadius: 12,
    paddingHorizontal: 14,
    backgroundColor: "#fff"
  },
  row: {
    flexDirection: "row",
    gap: 8,
    alignItems: "center"
  },
  flex: { flex: 1 },
  button: {
    minHeight: 48,
    borderRadius: 12,
    paddingHorizontal: 16,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "#101828"
  },
  buttonText: {
    color: "#fff",
    fontWeight: "700"
  },
  secondaryButton: {
    minHeight: 44,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: "#d0d5dd",
    alignItems: "center",
    justifyContent: "center"
  },
  disabledButton: {
    opacity: 0.55
  },
  error: { color: "#b42318" },
  notice: { color: "#175cd3" },
  conversation: {
    borderTopWidth: 1,
    borderTopColor: "#eaecf0",
    paddingVertical: 12
  },
  conversationTitle: {
    fontSize: 16,
    fontWeight: "600"
  }
});
