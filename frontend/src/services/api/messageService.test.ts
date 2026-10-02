import { afterEach, describe, expect, it, vi } from "vitest";
import {
  getMessageErrorMessage,
  listMessages,
  type ConversationMessage
} from "./messageService";
import { clearAccessToken, setAccessToken } from "../auth/accessTokenStore";

const message: ConversationMessage = {
  id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  conversationId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  role: "speaker",
  originalText: "Could you tell me where the station is?",
  translation: "İstasyonun nerede olduğunu söyleyebilir misiniz?",
  explanation: null,
  suggestedAnswer: null,
  suggestedAnswerTranslation: null,
  responseType: "question",
  questionDetected: true,
  questionDirectedAtUser: false,
  providerName: "gemini",
  createdAt: "2026-10-02T12:01:00Z"
};

describe("messageService", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("lists messages through the authenticated API client", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify([message]), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(
      listMessages(message.conversationId)
    ).resolves.toEqual([message]);

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/messages"
    );

    const [, init] = fetchMock.mock.calls[0];
    expect(new Headers(init?.headers).get("Authorization")).toBe(
      "Bearer test-token"
    );
  });

  it("rejects malformed message history", async () => {
    setAccessToken("test-token");

    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify([{ id: "missing-fields" }]), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      })
    );

    await expect(listMessages(message.conversationId)).rejects.toThrow(
      "The message history contains invalid data."
    );
  });

  it("maps unexpected failures to a safe fallback", () => {
    expect(
      getMessageErrorMessage(
        new Error("Unexpected internal implementation detail")
      )
    ).toBe("The message history could not be loaded.");
  });
});
