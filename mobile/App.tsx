import { useEffect, useState } from "react";
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
import {
  restoreSession,
  signIn,
  signOut,
  type AuthSession
} from "./src/auth/authService";

export default function App() {
  const [session, setSession] = useState<AuthSession | null>(null);
  const [checking, setChecking] = useState(true);
  const [token, setToken] = useState("");
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void restoreSession()
      .then(setSession)
      .catch(() => setSession(null))
      .finally(() => setChecking(false));
  }, []);

  useEffect(() => {
    if (!session) return;

    void listConversations()
      .then(setConversations)
      .catch((reason: unknown) =>
        setError(
          reason instanceof Error
            ? reason.message
            : "Conversations could not be loaded."
        )
      );
  }, [session]);

  if (checking) {
    return (
      <SafeAreaView style={styles.center}>
        <ActivityIndicator />
        <Text style={styles.muted}>Checking session…</Text>
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
            Enter an existing access token. It is stored only in the device
            secure store.
          </Text>
          <TextInput
            value={token}
            onChangeText={setToken}
            placeholder="Access token"
            autoCapitalize="none"
            autoCorrect={false}
            secureTextEntry
            style={styles.input}
          />
          {error ? <Text style={styles.error}>{error}</Text> : null}
          <Pressable
            style={styles.button}
            onPress={() => {
              setError(null);
              void signIn(token.trim())
                .then(setSession)
                .catch((reason: unknown) =>
                  setError(
                    reason instanceof Error ? reason.message : "Sign in failed."
                  )
                );
            }}
            disabled={!token.trim()}
          >
            <Text style={styles.buttonText}>Continue</Text>
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
        <Text style={styles.muted}>Signed in as {session.userId}</Text>

        <View style={styles.row}>
          <TextInput
            value={title}
            onChangeText={setTitle}
            placeholder="New conversation"
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
                    reason instanceof Error
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

        {error ? <Text style={styles.error}>{error}</Text> : null}

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
          style={styles.secondaryButton}
          onPress={() => {
            void signOut().then(() => {
              setSession(null);
              setConversations([]);
            });
          }}
        >
          <Text>Sign out</Text>
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
  error: { color: "#b42318" },
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
