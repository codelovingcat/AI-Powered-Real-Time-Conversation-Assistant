import { apiPost, ApiError } from "./apiClient";
import type { ConversationMessage } from "./messageService";

export type AssistantInputKind = "heardSpeech" | "userFormulationRequest";

export interface ProcessAssistantInput {
  inputKind: AssistantInputKind;
  text: string;
}

function isConversationMessage(value: unknown): value is ConversationMessage {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;

  return (
    typeof candidate.id === "string" &&
    typeof candidate.conversationId === "string" &&
    ["speaker", "user", "assistant", "system"].includes(
      candidate.role as string
    ) &&
    typeof candidate.originalText === "string" &&
    (candidate.translation === null || typeof candidate.translation === "string") &&
    (candidate.explanation === null || typeof candidate.explanation === "string") &&
    (candidate.suggestedAnswer === null ||
      typeof candidate.suggestedAnswer === "string") &&
    (candidate.suggestedAnswerTranslation === null ||
      typeof candidate.suggestedAnswerTranslation === "string") &&
    (candidate.responseType === null ||
      ["translation", "question", "answer", "instruction"].includes(
        candidate.responseType as string
      )) &&
    typeof candidate.questionDetected === "boolean" &&
    typeof candidate.questionDirectedAtUser === "boolean" &&
    (candidate.providerName === null ||
      typeof candidate.providerName === "string") &&
    typeof candidate.createdAt === "string"
  );
}

export async function processAssistantInput(
  conversationId: string,
  input: ProcessAssistantInput,
  signal?: AbortSignal
): Promise<ConversationMessage> {
  const response = await apiPost(
    `/api/conversations/${encodeURIComponent(conversationId)}/assistant`,
    input,
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
      case "network":
        return "The assistant could not be reached right now.";
      case "server":
        return "The assistant is temporarily unavailable.";
      case "rate_limit":
        return error.retryAfterSeconds
          ? `Too many requests. Try again in ${error.retryAfterSeconds} seconds.`
          : "Too many assistant requests. Please try again later.";
      default:
        return error.message;
    }
  }

  return "The assistant request could not be completed.";
}
