import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createConversation,
  deleteConversation,
  getConversationErrorMessage,
  listConversations
} from "./conversationService";
import { clearAccessToken, setAccessToken } from "../auth/accessTokenStore";

const conversation = {
  id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  title: "Hotel check-in",
  instruction: "Translate English into natural Turkish.",
  sourceLanguage: "en",
  targetLanguage: "tr",
  createdAt: "2026-10-02T12:00:00Z",
  updatedAt: "2026-10-02T12:00:00Z"
};

describe("conversationService", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("lists conversations through the authenticated API client", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify([conversation]), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(listConversations()).resolves.toEqual([conversation]);

    const [, init] = fetchMock.mock.calls[0];
    expect(new Headers(init?.headers).get("Authorization")).toBe(
      "Bearer test-token"
    );
  });

  it("creates a conversation and returns the validated response", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify(conversation), {
          status: 201,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(
      createConversation({
        title: "Hotel check-in",
        instruction: "Translate English into natural Turkish."
      })
    ).resolves.toEqual(conversation);

    expect(fetchMock.mock.calls[0][1]?.method).toBe("POST");
    expect(fetchMock.mock.calls[0][1]?.body).toBe(
      JSON.stringify({
        title: "Hotel check-in",
        instruction: "Translate English into natural Turkish."
      })
    );
  });

  it("deletes a conversation by id", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response("", { status: 204 }));

    await expect(
      deleteConversation("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
    ).resolves.toBeUndefined();

    expect(fetchMock.mock.calls[0][1]?.method).toBe("DELETE");
    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    );
  });

  it("rejects malformed list responses", async () => {
    setAccessToken("test-token");

    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify([{ id: "missing-fields" }]), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      })
    );

    await expect(listConversations()).rejects.toThrow(
      "The conversation list contains invalid data."
    );
  });

  it("maps common API failures to user-safe messages", () => {
    expect(
      getConversationErrorMessage(
        new Error("Unexpected internal implementation detail")
      )
    ).toBe("Unexpected internal implementation detail");
  });
});
