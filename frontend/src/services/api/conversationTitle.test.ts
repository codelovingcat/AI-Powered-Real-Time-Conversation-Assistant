import { afterEach, describe, expect, it, vi } from "vitest";
import { clearAccessToken, setAccessToken } from "../auth/accessTokenStore";
import { ApiError } from "./apiClient";
import { getConversation, getConversationErrorMessage, updateConversationTitle } from "./conversationService";

const conversation = {
  id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  title: "Original title",
  instruction: "Translate English into natural Turkish.",
  sourceLanguage: "en",
  targetLanguage: "tr",
  createdAt: "2026-10-17T12:00:00.000Z",
  updatedAt: "2026-10-17T12:01:00.000Z",
  lifecycleState: "ready" as const
};

describe("conversation title metadata", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("loads the latest conversation summary", async () => {
    setAccessToken("test-token");
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify(conversation), { status: 200, headers: { "Content-Type": "application/json" } })
    );

    await expect(getConversation(conversation.id)).resolves.toEqual(conversation);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    expect(fetchMock.mock.calls[0][1]?.method).toBe("GET");
  });

  it("sends the trimmed title with the version timestamp", async () => {
    setAccessToken("test-token");
    const updated = { ...conversation, title: "Updated title", updatedAt: "2026-10-17T12:02:00.000Z" };
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify(updated), { status: 200, headers: { "Content-Type": "application/json" } })
    );

    await expect(updateConversationTitle(conversation.id, "  Updated title ", conversation.updatedAt)).resolves.toEqual(updated);
    expect(fetchMock.mock.calls[0][1]?.method).toBe("PATCH");
    expect(fetchMock.mock.calls[0][1]?.body).toBe(JSON.stringify({ title: "Updated title", expectedUpdatedAt: conversation.updatedAt }));
  });

  it("rejects blank or overlong titles before making a request", async () => {
    setAccessToken("test-token");
    const fetchMock = vi.spyOn(globalThis, "fetch");
    await expect(updateConversationTitle(conversation.id, "   ", conversation.updatedAt)).rejects.toThrow("between 1 and 200 characters");
    await expect(updateConversationTitle(conversation.id, "x".repeat(201), conversation.updatedAt)).rejects.toThrow("between 1 and 200 characters");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows a safe recovery message after a conflict", () => {
    expect(getConversationErrorMessage(new ApiError("Conflict", 409, "client"))).toContain("latest details were reloaded");
  });
});
