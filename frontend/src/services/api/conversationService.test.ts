import { afterEach, describe, expect, it, vi } from "vitest";
import {
  createConversation,
  deleteConversation,
  updateConversationInstruction,
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
        new Response(JSON.stringify({ items: [conversation], nextCursor: null }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(listConversations()).resolves.toEqual({
      items: [conversation],
      nextCursor: null
    });

    expect(fetchMock.mock.calls[0][0]).toBe("/api/conversations?limit=50");

    const [, init] = fetchMock.mock.calls[0];
    expect(new Headers(init?.headers).get("Authorization")).toBe(
      "Bearer test-token"
    );
  });

  it("passes the cursor to the paginated conversation endpoint", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify({ items: [conversation], nextCursor: "next-cursor" }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(listConversations("cursor-value")).resolves.toEqual({
      items: [conversation],
      nextCursor: "next-cursor"
    });

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/conversations?limit=50&cursor=cursor-value"
    );
  });

  it("passes search and updated date filters to the paginated endpoint", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify({ items: [conversation], nextCursor: null }), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(
      listConversations(undefined, undefined, {
        search: "hotel check-in",
        updatedFrom: "2026-10-01T00:00:00.000Z",
        updatedTo: "2026-10-07T00:00:00.000Z"
      })
    ).resolves.toEqual({
      items: [conversation],
      nextCursor: null
    });

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/conversations?limit=50&search=hotel+check-in&updatedFrom=2026-10-01T00%3A00%3A00.000Z&updatedTo=2026-10-07T00%3A00%3A00.000Z"
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

  it("updates the conversation instruction", async () => {
    setAccessToken("test-token");

    const updated = {
      ...conversation,
      instruction: "Only translate what is being said."
    };

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify(updated), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    await expect(
      updateConversationInstruction(
        conversation.id,
        "Only translate what is being said."
      )
    ).resolves.toEqual(updated);

    expect(fetchMock.mock.calls[0][0]).toBe(
      "/api/conversations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    );
    expect(fetchMock.mock.calls[0][1]?.method).toBe("PATCH");
    expect(fetchMock.mock.calls[0][1]?.body).toBe(
      JSON.stringify({
        instruction: "Only translate what is being said."
      })
    );
  });

  it("rejects an invalid instruction update response", async () => {
    setAccessToken("test-token");

    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ id: conversation.id }), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      })
    );

    await expect(
      updateConversationInstruction(conversation.id, "Only translate.")
    ).rejects.toThrow("The updated conversation response is invalid.");
  });

  it("deletes a conversation by id", async () => {
    setAccessToken("test-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response(null, { status: 204 }));

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
      new Response(JSON.stringify({ items: [{ id: "missing-fields" }], nextCursor: null }), {
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
    ).toBe("The conversation request could not be completed.");
  });
});
