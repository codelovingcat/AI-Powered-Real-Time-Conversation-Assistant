import {
  apiDelete,
  apiGet,
  apiPatch,
  apiPost,
  ApiError
} from "./apiClient";

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
  if (typeof value !== "object" || value === null) {
    return false;
  }

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

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
}

export interface ConversationListFilters {
  search?: string;
  updatedFrom?: string;
  updatedTo?: string;
}

function parseConversationPage(value: unknown): CursorPage<ConversationSummary> {
  if (typeof value !== "object" || value === null) {
    throw new Error("The conversation list response is invalid.");
  }

  const candidate = value as Record<string, unknown>;

  if (!Array.isArray(candidate.items)) {
    throw new Error("The conversation list response is invalid.");
  }

  if (!candidate.items.every(isConversationSummary)) {
    throw new Error("The conversation list contains invalid data.");
  }

  if (candidate.nextCursor !== null && typeof candidate.nextCursor !== "string") {
    throw new Error("The conversation list response is invalid.");
  }

  return {
    items: candidate.items,
    nextCursor: candidate.nextCursor
  };
}

export interface CreateConversationInput {
  title: string;
  instruction: string;
  sourceLanguage?: string;
  targetLanguage?: string;
}

export async function listConversations(
  cursor?: string,
  signal?: AbortSignal,
  filters: ConversationListFilters = {}
): Promise<CursorPage<ConversationSummary>> {
  const params = new URLSearchParams({ limit: "50" });

  if (cursor) {
    params.set("cursor", cursor);
  }

  const search = filters.search?.trim();
  if (search) {
    params.set("search", search);
  }

  if (filters.updatedFrom) {
    params.set("updatedFrom", filters.updatedFrom);
  }

  if (filters.updatedTo) {
    params.set("updatedTo", filters.updatedTo);
  }

  const response = await apiGet("/api/conversations?" + params.toString(), signal);
  return parseConversationPage(await response.json());
}

export async function createConversation(
  input: CreateConversationInput,
  signal?: AbortSignal
): Promise<ConversationSummary> {
  const response = await apiPost("/api/conversations", input, signal);
  const payload: unknown = await response.json();

  if (!isConversationSummary(payload)) {
    throw new Error("The created conversation response is invalid.");
  }

  return payload;
}

export async function updateConversationInstruction(
  conversationId: string,
  instruction: string,
  signal?: AbortSignal
): Promise<ConversationSummary> {
  const response = await apiPatch(
    `/api/conversations/${encodeURIComponent(conversationId)}`,
    { instruction },
    signal
  );

  const payload: unknown = await response.json();

  if (!isConversationSummary(payload)) {
    throw new Error("The updated conversation response is invalid.");
  }

  return payload;
}

export async function deleteConversation(
  conversationId: string,
  signal?: AbortSignal
): Promise<void> {
  await apiDelete(
    `/api/conversations/${encodeURIComponent(conversationId)}`,
    signal
  );
}

export function getConversationErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.kind) {
      case "unauthorized":
        return "Your session has expired. Please sign in again.";
      case "network":
        return "Conversations could not be loaded right now.";
      case "server":
        return "The conversation service is temporarily unavailable.";
      case "rate_limit":
        return error.retryAfterSeconds
          ? `Too many requests. Try again in ${error.retryAfterSeconds} seconds.`
          : error.message;
      default:
        return error.message;
    }
  }

  return "The conversation request could not be completed.";
}
