import { afterEach, describe, expect, it, vi } from "vitest";
import {
  getAssistantErrorMessage,
  processAssistantInput
} from "./assistantService";
import { clearAccessToken, setAccessToken } from "../auth/accessTokenStore";

const conversationId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

const response = {
  id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  conversationId,
  role: "speaker",
  originalText: "Where is the station?",
  translation: "İstasyon nerede?",
  explanation: "A direct question about the station location.",
  suggestedAnswer: null,
  suggestedAnswerTranslation: null,
  responseType: "question",
  questionDetected: true,
  questionDirectedAtUser: false,
  providerName: "gemini",
  createdAt: "2026-10-02T15:00:00Z"
};

describe("assistantService", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("sends heard speech through the authenticated assistant endpoint", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify(response), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(
      processAssistantInput(conversationId, {
        inputKind: "heardSpeech",
        text: "Where is the station?"
      })
    ).resolves.toEqual(response);

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/assistant"
    );
    expect(fetchMock.mock.calls[0][1]?.method).toBe("POST");
    expect(fetchMock.mock.calls[0][1]?.body).toBe(
      JSON.stringify({
        inputKind: "heardSpeech",
        text: "Where is the station?"
      })
    );
    expect(
      new Headers(fetchMock.mock.calls[0][1]?.headers).get("Authorization")
    ).toBe("Bearer test-token");
  });

  it("sends user formulation requests with the correct input kind", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify({ ...response, role: "user" }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await processAssistantInput(conversationId, {
      inputKind: "userFormulationRequest",
      text: "How can I ask where the station is?"
    });

    expect(JSON.parse(fetchMock.mock.calls[0][1]?.body as string)).toEqual({
      inputKind: "userFormulationRequest",
      text: "How can I ask where the station is?"
    });
  });

  it("rejects malformed assistant responses", async () => {
    setAccessToken("test-token");

    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ id: response.id }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      })
    );

    await expect(
      processAssistantInput(conversationId, {
        inputKind: "heardSpeech",
        text: "Hello"
      })
    ).rejects.toThrow("The assistant response is invalid.");
  });

  it("maps failures to safe messages", () => {
    expect(
      getAssistantErrorMessage(new Error("secret provider implementation"))
    ).toBe("The assistant request could not be completed.");
  });
});
