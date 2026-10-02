import { apiPost, ApiError } from "./apiClient";
import type { ConversationMessage } from "./messageService";

export type AssistantInputKind =
  | "heardSpeech"
  | "userFormulationRequest";

export async function processAssistantInput(
  conversationId: string,
  inputKind: AssistantInputKind,
  text: string,
  signal?: AbortSignal
): Promise<ConversationMessage> {
  const response = await apiPost(
    `/api/conversations/${encodeURIComponent(conversationId)}/assistant`,
    { inputKind, text },
    signal
  );

  const payload: unknown = await response.json();

  if (!isConversationMessage(payload)) {
    throw new Error("The assistant response is invalid.");
  }

  return payload;
}

export function getAssistantErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.kind) {
      case "unauthorized":
        return "Your session has expired. Please sign in again.";
      case "rate_limit":
        return error.retryAfterSeconds
          ? `AI rate limit reached. Try again in ${error.retryAfterSeconds} seconds.`
          : "AI rate limit reached. Please try again later.";
      case "server":
        return "The AI service is temporarily unavailable. Please try again.";
      case "network":
        return "The AI service could not be reached. Check your connection and try again.";
      default:
        return error.message;
    }
  }

  return error instanceof Error
    ? error.message
    : "The AI response could not be completed.";
}

function isConversationMessage(value: unknown): value is ConversationMessage {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;

  return (
    typeof candidate.id === "string" &&
    typeof candidate.conversationId === "string" &&
    typeof candidate.role === "string" &&
    typeof candidate.originalText === "string" &&
    (candidate.translation === null || typeof candidate.translation === "string") &&
    (candidate.explanation === null || typeof candidate.explanation === "string") &&
    (candidate.suggestedAnswer === null || typeof candidate.suggestedAnswer === "string") &&
    (candidate.suggestedAnswerTranslation === null ||
      typeof candidate.suggestedAnswerTranslation === "string") &&
    (candidate.responseType === null || typeof candidate.responseType === "string") &&
    typeof candidate.questionDetected === "boolean" &&
    typeof candidate.questionDirectedAtUser === "boolean" &&
    (candidate.providerName === null || typeof candidate.providerName === "string") &&
    typeof candidate.createdAt === "string"
  );
}
