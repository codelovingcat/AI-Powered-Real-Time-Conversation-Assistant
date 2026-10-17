import { apiGet, apiRequest } from "./apiClient";

export type ResponseLength = "concise" | "balanced" | "detailed";
export type AssistantTone = "casual" | "contextual" | "formal";
export type ExplanationLevel = "minimal" | "balanced" | "detailed";

export interface AssistantPreferences {
  responseLength: ResponseLength;
  tone: AssistantTone;
  explanationLevel: ExplanationLevel;
  suggestReplies: boolean;
}

export const DEFAULT_ASSISTANT_PREFERENCES: AssistantPreferences = {
  responseLength: "balanced",
  tone: "contextual",
  explanationLevel: "balanced",
  suggestReplies: true
};

function isResponseLength(value: unknown): value is ResponseLength {
  return value === "concise" || value === "balanced" || value === "detailed";
}

function isTone(value: unknown): value is AssistantTone {
  return value === "casual" || value === "contextual" || value === "formal";
}

function isExplanationLevel(value: unknown): value is ExplanationLevel {
  return value === "minimal" || value === "balanced" || value === "detailed";
}

function parseAssistantPreferences(value: unknown): AssistantPreferences {
  if (typeof value !== "object" || value === null) {
    throw new Error("The assistant preferences response is invalid.");
  }

  const candidate = value as Record<string, unknown>;
  if (
    !isResponseLength(candidate.responseLength) ||
    !isTone(candidate.tone) ||
    !isExplanationLevel(candidate.explanationLevel) ||
    typeof candidate.suggestReplies !== "boolean"
  ) {
    throw new Error("The assistant preferences response is invalid.");
  }

  return {
    responseLength: candidate.responseLength,
    tone: candidate.tone,
    explanationLevel: candidate.explanationLevel,
    suggestReplies: candidate.suggestReplies
  };
}

export async function getAssistantPreferences(
  signal?: AbortSignal
): Promise<AssistantPreferences> {
  const response = await apiGet("/api/assistant-preferences", signal);
  return parseAssistantPreferences(await response.json());
}

export async function saveAssistantPreferences(
  preferences: AssistantPreferences,
  signal?: AbortSignal
): Promise<AssistantPreferences> {
  const response = await apiRequest(
    "/api/assistant-preferences",
    {
      method: "PUT",
      body: JSON.stringify(preferences),
      signal
    }
  );

  return parseAssistantPreferences(await response.json());
}
