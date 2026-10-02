import {
  apiDelete,
  apiGet,
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

function parseConversationList(value: unknown): ConversationSummary[] {
  if (!Array.isArray(value)) {
    throw new Error("The conversation list response is invalid.");
  }

  if (!value.every(isConversationSummary)) {
    throw new Error("The conversation list contains invalid data.");
  }

  return value;
}

export interface CreateConversationInput {
  title: string;
  instruction: string;
  sourceLanguage?: string;
  targetLanguage?: string;
}

export async function listConversations(
  signal?: AbortSignal
): Promise<ConversationSummary[]> {
  const response = await apiGet("/api/conversations", signal);
  return parseConversationList(await response.json());
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

  return error instanceof Error
    ? error.message
    : "The conversation request could not be completed.";
}
