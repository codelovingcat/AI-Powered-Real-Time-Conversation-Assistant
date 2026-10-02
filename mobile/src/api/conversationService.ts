import { apiGet, apiPost } from "./apiClient";

export interface ConversationSummary {
  id: string;
  title: string;
  instruction: string;
  sourceLanguage: string;
  targetLanguage: string;
  createdAt: string;
  updatedAt: string;
}

function isConversationSummary(value: unknown): value is ConversationSummary {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Record<string, unknown>;

  return (
    typeof candidate.id === "string" &&
    typeof candidate.title === "string" &&
    typeof candidate.instruction === "string" &&
    typeof candidate.sourceLanguage === "string" &&
    typeof candidate.targetLanguage === "string" &&
    typeof candidate.createdAt === "string" &&
    typeof candidate.updatedAt === "string"
  );
}

export async function listConversations(): Promise<ConversationSummary[]> {
  const response = await apiGet("/api/conversations");
  const payload: unknown = await response.json();

  if (!Array.isArray(payload) || !payload.every(isConversationSummary)) {
    throw new Error("The conversation list response is invalid.");
  }

  return payload;
}

export async function createConversation(
  title: string,
  instruction = ""
): Promise<ConversationSummary> {
  const response = await apiPost("/api/conversations", {
    title,
    instruction,
    sourceLanguage: "en",
    targetLanguage: "tr"
  });

  const payload: unknown = await response.json();
  if (!isConversationSummary(payload)) {
    throw new Error("The created conversation response is invalid.");
  }

  return payload;
}
