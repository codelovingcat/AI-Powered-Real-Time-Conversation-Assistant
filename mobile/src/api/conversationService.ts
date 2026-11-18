import { apiDelete, apiGet, apiPost } from "./apiClient";

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

export interface ConversationListFilters {
  search?: string;
  updatedFrom?: string;
  updatedTo?: string;
}

const CONVERSATION_STATES: readonly ConversationLifecycleState[] = [
  "ready",
  "active",
  "paused",
  "completed"
];
const MESSAGE_ROLES = ["speaker", "user", "assistant", "system"] as const;
const RESPONSE_TYPES = ["translation", "question", "answer", "instruction"] as const;
const MESSAGE_INTENTS = [
  "statement",
  "questionDirectedAtUser",
  "questionDirectedAtOthers",
  "rhetoricalQuestion",
  "requestDirectedAtUser",
  "requestDirectedAtOthers",
  "userFormulationRequest",
  "ambiguous"
] as const;

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
    CONVERSATION_STATES.includes(candidate.lifecycleState as ConversationLifecycleState)
  );
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === "string";
}

function isConversationMessage(value: unknown): value is ConversationMessage {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Record<string, unknown>;

  return (
    typeof candidate.id === "string" &&
    typeof candidate.conversationId === "string" &&
    MESSAGE_ROLES.includes(candidate.role as (typeof MESSAGE_ROLES)[number]) &&
    typeof candidate.originalText === "string" &&
    isNullableString(candidate.translation) &&
    isNullableString(candidate.explanation) &&
    isNullableString(candidate.suggestedAnswer) &&
    isNullableString(candidate.suggestedAnswerTranslation) &&
    (candidate.responseType === null ||
      RESPONSE_TYPES.includes(candidate.responseType as (typeof RESPONSE_TYPES)[number])) &&
    typeof candidate.questionDetected === "boolean" &&
    typeof candidate.questionDirectedAtUser === "boolean" &&
    isNullableString(candidate.providerName) &&
    typeof candidate.createdAt === "string" &&
    (candidate.intent === undefined ||
      candidate.intent === null ||
      MESSAGE_INTENTS.includes(candidate.intent as (typeof MESSAGE_INTENTS)[number]))
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

export async function listConversations(
  cursor?: string,
  signal?: AbortSignal,
  filters: ConversationListFilters = {},
  limit = 50
): Promise<CursorPage<ConversationSummary>> {
  const boundedLimit = Math.max(1, Math.min(100, Math.floor(limit)));
  const params = new URLSearchParams({ limit: String(boundedLimit) });

  if (cursor) {
    params.set("cursor", cursor);
  }

  const search = filters.search?.trim();
  if (search) {
    if (search.length > 100) {
      throw new Error("Conversation search must be 100 characters or fewer.");
    }
    params.set("search", search);
  }

  if (filters.updatedFrom) {
    params.set("updatedFrom", filters.updatedFrom);
  }

  if (filters.updatedTo) {
    params.set("updatedTo", filters.updatedTo);
  }

  const response = await apiGet("/api/conversations?" + params.toString(), signal);
  return parsePage(
    await response.json(),
    isConversationSummary,
    "The conversation list response is invalid."
  );
}

export async function getConversation(
  conversationId: string,
  signal?: AbortSignal
): Promise<ConversationSummary> {
  const response = await apiGet(
    "/api/conversations/" + encodeURIComponent(conversationId),
    signal
  );
  const payload: unknown = await response.json();

  if (!isConversationSummary(payload)) {
    throw new Error("The conversation response is invalid.");
  }

  return payload;
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
  conversationId: string,
  cursor?: string,
  signal?: AbortSignal,
  limit = 50
): Promise<CursorPage<ConversationMessage>> {
  const boundedLimit = Math.max(1, Math.min(100, Math.floor(limit)));
  const params = new URLSearchParams({ limit: String(boundedLimit) });

  if (cursor) {
    params.set("cursor", cursor);
  }

  const response = await apiGet(
    "/api/conversations/" +
      encodeURIComponent(conversationId) +
      "/messages?" +
      params.toString(),
    signal
  );

  return parsePage(
    await response.json(),
    isConversationMessage,
    "The conversation history response is invalid."
  );
}

export async function deleteConversation(
  conversationId: string,
  signal?: AbortSignal
): Promise<void> {
  await apiDelete(
    "/api/conversations/" + encodeURIComponent(conversationId),
    signal
  );
}
