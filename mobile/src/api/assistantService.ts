import { apiPost } from "./apiClient";
import type { ConversationMessage } from "./conversationService";

export async function processAssistantInput(
  conversationId: string,
  text: string
): Promise<ConversationMessage> {
  const response = await apiPost(
    "/api/conversations/" + encodeURIComponent(conversationId) + "/assistant",
    {
      inputKind: "heardSpeech",
      text: text.trim()
    }
  );

  const payload: unknown = await response.json();
  if (typeof payload !== "object" || payload === null) {
    throw new Error("The assistant response is invalid.");
  }

  const candidate = payload as Record<string, unknown>;
  if (
    typeof candidate.id !== "string" ||
    typeof candidate.conversationId !== "string" ||
    typeof candidate.originalText !== "string" ||
    typeof candidate.createdAt !== "string" ||
    typeof candidate.questionDetected !== "boolean" ||
    typeof candidate.questionDirectedAtUser !== "boolean"
  ) {
    throw new Error("The assistant response is invalid.");
  }

  return candidate as unknown as ConversationMessage;
}
