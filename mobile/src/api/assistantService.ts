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
  const nullableStrings = [
    "translation",
    "explanation",
    "suggestedAnswer",
    "suggestedAnswerTranslation",
    "responseType",
    "providerName",
    "intent"
  ];

  if (
    typeof candidate.id !== "string" ||
    typeof candidate.conversationId !== "string" ||
    !["speaker", "user", "assistant", "system"].includes(candidate.role as string) ||
    typeof candidate.originalText !== "string" ||
    !nullableStrings.every((key) =>
      candidate[key] === null ||
      candidate[key] === undefined ||
      typeof candidate[key] === "string"
    ) ||
    typeof candidate.questionDetected !== "boolean" ||
    typeof candidate.questionDirectedAtUser !== "boolean" ||
    typeof candidate.createdAt !== "string"
  ) {
    throw new Error("The assistant response is invalid.");
  }

  return candidate as unknown as ConversationMessage;
}
