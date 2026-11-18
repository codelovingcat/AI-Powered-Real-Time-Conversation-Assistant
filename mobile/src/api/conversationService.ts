import { apiGet, apiPost } from "./apiClient";

export type ConversationLifecycleState = "ready" | "active" | "paused" | "completed";
export type ConversationLifecycleAction = "start" | "pause" | "resume" | "finish";

export interface ConversationSummary {
  id: string;
  title: string;
  instruction: string;
  sourceLanguage: string;
  targetLanguage: string;
  createdAt: string;
  updatedAt: string;
  lifecycleState: ConversationLifecycleState;
}

export interface ConversationMessage {
  id: string;
  conversationId: string;
  role: "speaker" | "user" | "assistant" | "system";
  originalText: string;
  translation: string | null;
  explanation: string | null;
  suggestedAnswer: string | null;
  suggestedAnswerTranslation: string | null;
  responseType: "translation" | "question" | "answer" | "instruction" | null;
  questionDetected: boolean;
  questionDirectedAtUser: boolean;
  providerName: string | null;
  createdAt: string;
  intent?: string | null;
}

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
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
    typeof candidate.updatedAt === "string" &&
    (candidate.lifecycleState === "ready" ||
      candidate.lifecycleState === "active" ||
      candidate.lifecycleState === "paused" ||
      candidate.lifecycleState === "completed")
  );
}

function isConversationMessage(value: unknown): value is ConversationMessage {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Record<string, unknown>;

  return (
    typeof candidate.id === "string" &&
    typeof candidate.conversationId === "string" &&
    ["speaker", "user", "assistant", "system"].includes(candidate.role as string) &&
    typeof candidate.originalText === "string" &&
    (candidate.translation === null || typeof candidate.translation === "string") &&
    (candidate.explanation === null || typeof candidate.explanation === "string") &&
    (candidate.suggestedAnswer === null || typeof candidate.suggestedAnswer === "string") &&
    (candidate.suggestedAnswerTranslation === null ||
      typeof candidate.suggestedAnswerTranslation === "string") &&
    (candidate.responseType === null ||
      ["translation", "question", "answer", "instruction"].includes(candidate.responseType as string)) &&
    typeof candidate.questionDetected === "boolean" &&
    typeof candidate.questionDirectedAtUser === "boolean" &&
    (candidate.providerName === null || typeof candidate.providerName === "string") &&
    typeof candidate.createdAt === "string"
  );
}

function parsePage<T>(
  payload: unknown,
  isItem: (value: unknown) => value is T,
  message: string
): CursorPage<T> {
  if (typeof payload !== "object" || payload === null) {
    throw new Error(message);
  }

  const candidate = payload as Record<string, unknown>;
  if (
    !Array.isArray(candidate.items) ||
    !candidate.items.every(isItem) ||
    (candidate.nextCursor !== null && typeof candidate.nextCursor !== "string")
  ) {
    throw new Error(message);
  }

  return {
    items: candidate.items,
    nextCursor: candidate.nextCursor
  };
}

export async function listConversations(): Promise<CursorPage<ConversationSummary>> {
  const response = await apiGet("/api/conversations?limit=50");
  return parsePage(
    await response.json(),
    isConversationSummary,
    "The conversation list response is invalid."
  );
}

export async function createConversation(
  title: string,
  instruction = "Translate the English speaker's speech into natural and accurate Turkish. Explain briefly when it helps understanding."
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

export async function transitionConversationLifecycle(
  conversationId: string,
  action: ConversationLifecycleAction
): Promise<ConversationSummary> {
  const response = await apiPost(
    "/api/conversations/" + encodeURIComponent(conversationId) + "/lifecycle",
    { action }
  );
  const payload: unknown = await response.json();

  if (!isConversationSummary(payload)) {
    throw new Error("The conversation lifecycle response is invalid.");
  }

  return payload;
}

export async function listMessages(
  conversationId: string
): Promise<CursorPage<ConversationMessage>> {
  const response = await apiGet(
    "/api/conversations/" + encodeURIComponent(conversationId) + "/messages?limit=50"
  );

  return parsePage(
    await response.json(),
    isConversationMessage,
    "The conversation history response is invalid."
  );
}
