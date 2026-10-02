import { afterEach, describe, expect, it, vi } from "vitest";
import { clearAccessToken, setAccessToken } from "../auth/accessTokenStore";
import { ApiError } from "./apiClient";
import {
  getAssistantErrorMessage,
  processAssistantInput
} from "./assistantService";

const message = {
  id: "message-1",
  conversationId: "conversation-1",
  role: "speaker",
  originalText: "Would you like some coffee?",
  translation: "Kahve ister misin?",
  explanation: "A polite offer.",
  suggestedAnswer: "Yes, please. Thank you.",
  suggestedAnswerTranslation: "Evet, lütfen. Teşekkür ederim.",
  responseType: "question",
  questionDetected: true,
  questionDirectedAtUser: true,
  providerName: "gemini",
  createdAt: "2026-10-02T12:00:00Z"
};

describe("assistantService", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("posts a heard speech input with the authenticated request", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify(message), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    const result = await processAssistantInput(
      "conversation-1",
      "heardSpeech",
      "Would you like some coffee?"
    );

    expect(result).toEqual(message);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/conversations/conversation-1/assistant");
    expect(init?.method).toBe("POST");
    expect(init?.body).toBe(
      JSON.stringify({
        inputKind: "heardSpeech",
        text: "Would you like some coffee?"
      })
    );

    const headers = new Headers(init?.headers);
    expect(headers.get("Authorization")).toBe("Bearer test-token");
  });

  it("maps AI rate limits to a user-facing retry message", () => {
    expect(
      getAssistantErrorMessage(new ApiError("Too many requests.", 429, "rate_limit", 9))
    ).toContain("Too many requests");
  });
});
