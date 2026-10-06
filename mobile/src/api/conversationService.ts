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

export interface ConversationPage {
  items: ConversationSummary[];
  nextCursor: string | null;
}

export async function listConversations(): Promise<ConversationPage> {
  const response = await apiGet("/api/conversations?limit=50");
  const payload: unknown = await response.json();

  if (typeof payload !== "object" || payload === null) {
    throw new Error("The conversation list response is invalid.");
  }

  const candidate = payload as Record<string, unknown>;

  if (
    !Array.isArray(candidate.items) ||
    !candidate.items.every(isConversationSummary) ||
    (candidate.nextCursor !== null && typeof candidate.nextCursor !== "string")
  ) {
    throw new Error("The conversation list response is invalid.");
  }

  return {
    items: candidate.items,
    nextCursor: candidate.nextCursor
  };
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
