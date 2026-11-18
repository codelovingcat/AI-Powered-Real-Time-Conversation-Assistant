import { beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  apiDelete: vi.fn(),
  apiGet: vi.fn(),
  apiPost: vi.fn()
}));

vi.mock("./apiClient", () => api);

import {
  deleteConversation,
  getConversation,
  listConversations,
  listMessages,
  type ConversationMessage,
  type ConversationSummary
} from "./conversationService";

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" }
  });
}

const conversation: ConversationSummary = {
  id: "conversation-1",
  title: "Job interview",
  instruction: "Translate English into Turkish.",
  sourceLanguage: "en",
  targetLanguage: "tr",
  createdAt: "2026-11-01T10:00:00.000Z",
  updatedAt: "2026-11-02T10:00:00.000Z",
  lifecycleState: "completed"
};

const message: ConversationMessage = {
  id: "message-1",
  conversationId: "conversation-1",
  role: "speaker",
  originalText: "Tell me about your experience.",
  translation: "Bana deneyiminizden bahsedin.",
  explanation: null,
  suggestedAnswer: null,
  suggestedAnswerTranslation: null,
  responseType: "translation",
  questionDetected: true,
  questionDirectedAtUser: true,
  providerName: "gemini",
  createdAt: "2026-11-02T10:00:00.000Z",
  intent: "questionDirectedAtUser"
};

function queryFromCall(callIndex = 0): URLSearchParams {
  const path = String(api.apiGet.mock.calls[callIndex]?.[0] ?? "");
  return new URL(path, "https://conversa.test").searchParams;
}

describe("mobile conversation history API", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads a bounded first conversation page with server-side search and date filters", async () => {
    api.apiGet.mockResolvedValueOnce(jsonResponse({
      items: [conversation],
      nextCursor: "next-page-token"
    }));

    const controller = new AbortController();
    const page = await listConversations(
      undefined,
      controller.signal,
      {
        search: "  interview ",
        updatedFrom: "2026-11-01T00:00:00.000Z",
        updatedTo: "2026-11-04T00:00:00.000Z"
      },
      30
    );

    expect(page.items).toEqual([conversation]);
    expect(page.nextCursor).toBe("next-page-token");
    expect(api.apiGet).toHaveBeenCalledOnce();
    expect(queryFromCall().get("limit")).toBe("30");
    expect(queryFromCall().get("search")).toBe("interview");
    expect(queryFromCall().get("updatedFrom")).toBe("2026-11-01T00:00:00.000Z");
    expect(queryFromCall().get("updatedTo")).toBe("2026-11-04T00:00:00.000Z");
    expect(api.apiGet.mock.calls[0]?.[1]).toBe(controller.signal);
  });

  it("preserves the active search filters while requesting the next cursor page", async () => {
    api.apiGet.mockResolvedValueOnce(jsonResponse({
      items: [conversation],
      nextCursor: null
    }));

    const page = await listConversations(
      "opaque-cursor",
      undefined,
      { search: "interview" },
      30
    );

    expect(page.nextCursor).toBeNull();
    expect(queryFromCall().get("cursor")).toBe("opaque-cursor");
    expect(queryFromCall().get("search")).toBe("interview");
    expect(queryFromCall().get("limit")).toBe("30");
  });

  it("rejects invalid conversation pages instead of trusting remote payloads", async () => {
    api.apiGet.mockResolvedValueOnce(jsonResponse({
      items: [{ id: "conversation-1", title: "Incomplete" }],
      nextCursor: null
    }));

    await expect(listConversations()).rejects.toThrow(
      "The conversation list response is invalid."
    );
  });

  it("loads a conversation detail through the owner-authorized API route", async () => {
    api.apiGet.mockResolvedValueOnce(jsonResponse(conversation));

    const result = await getConversation("conversation/with spaces");

    expect(result).toEqual(conversation);
    expect(api.apiGet.mock.calls[0]?.[0]).toBe(
      "/api/conversations/conversation%2Fwith%20spaces"
    );
  });

  it("loads the next older message page using its cursor and conversation scope", async () => {
    api.apiGet.mockResolvedValueOnce(jsonResponse({
      items: [message],
      nextCursor: "older-message-cursor"
    }));

    const page = await listMessages("conversation-1", "older-cursor", undefined, 30);

    expect(page.items).toEqual([message]);
    expect(page.nextCursor).toBe("older-message-cursor");
    expect(api.apiGet.mock.calls[0]?.[0]).toBe(
      "/api/conversations/conversation-1/messages?limit=30&cursor=older-cursor"
    );
  });

  it("deletes a conversation using the authenticated owner-scoped endpoint", async () => {
    api.apiDelete.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await expect(deleteConversation("conversation-1")).resolves.toBeUndefined();

    expect(api.apiDelete).toHaveBeenCalledWith(
      "/api/conversations/conversation-1",
      undefined
    );
  });

  it("caps page sizes to the backend maximum", async () => {
    api.apiGet.mockResolvedValueOnce(jsonResponse({
      items: [],
      nextCursor: null
    }));

    await listMessages("conversation-1", undefined, undefined, 500);

    expect(queryFromCall().get("limit")).toBe("100");
  });
});
