import { apiGet, ApiError } from "./apiClient";

export type MessageRole = "speaker" | "user" | "assistant" | "system";
export type AiResponseType =
  | "translation"
  | "question"
  | "answer"
  | "instruction";

export interface ConversationMessage {
  id: string;
  conversationId: string;
  role: MessageRole;
  originalText: string;
  translation: string | null;
  explanation: string | null;
  suggestedAnswer: string | null;
  suggestedAnswerTranslation: string | null;
  responseType: AiResponseType | null;
  questionDetected: boolean;
  questionDirectedAtUser: boolean;
  providerName: string | null;
  createdAt: string;
}

function isEnumValue<T extends string>(
  value: unknown,
  values: readonly T[]
): value is T {
  return typeof value === "string" && values.includes(value as T);
}

function isConversationMessage(value: unknown): value is ConversationMessage {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;

  return (
    typeof candidate.id === "string" &&
    typeof candidate.conversationId === "string" &&
    isEnumValue(candidate.role, ["speaker", "user", "assistant", "system"]) &&
    typeof candidate.originalText === "string" &&
    (candidate.translation === null ||
      typeof candidate.translation === "string") &&
    (candidate.explanation === null ||
      typeof candidate.explanation === "string") &&
    (candidate.suggestedAnswer === null ||
      typeof candidate.suggestedAnswer === "string") &&
    (candidate.suggestedAnswerTranslation === null ||
      typeof candidate.suggestedAnswerTranslation === "string") &&
    (candidate.responseType === null ||
      isEnumValue(candidate.responseType, [
        "translation",
        "question",
        "answer",
        "instruction"
      ])) &&
    typeof candidate.questionDetected === "boolean" &&
    typeof candidate.questionDirectedAtUser === "boolean" &&
    (candidate.providerName === null ||
      typeof candidate.providerName === "string") &&
    typeof candidate.createdAt === "string"
  );
}

function parseMessageList(value: unknown): ConversationMessage[] {
  if (!Array.isArray(value)) {
    throw new Error("The message history response is invalid.");
  }

  if (!value.every(isConversationMessage)) {
    throw new Error("The message history contains invalid data.");
  }

  return value;
}

export async function listMessages(
  conversationId: string,
  signal?: AbortSignal
): Promise<ConversationMessage[]> {
  const response = await apiGet(
    `/api/conversations/${encodeURIComponent(conversationId)}/messages`,
    signal
  );

  return parseMessageList(await response.json());
}

export function getMessageErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.kind) {
      case "unauthorized":
        return "Your session has expired. Please sign in again.";
      case "network":
        return "Message history could not be loaded right now.";
      case "server":
        return "The message history service is temporarily unavailable.";
      case "rate_limit":
        return error.retryAfterSeconds
          ? `Too many requests. Try again in ${error.retryAfterSeconds} seconds.`
          : error.message;
      default:
        return error.message;
    }
  }

  return "The message history could not be loaded.";
}
