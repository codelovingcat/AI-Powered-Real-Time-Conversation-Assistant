import { afterEach, describe, expect, it, vi } from "vitest";
import {
  apiDelete,
  apiGet,
  apiPatch,
  apiPost
} from "./apiClient";
import {
  clearAccessToken,
  getAccessToken,
  setAccessToken
} from "../auth/accessTokenStore";
import { subscribeToUnauthorized } from "../auth/authEvents";

const refreshPayload = {
  authenticated: true,
  userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  accessToken: "refreshed-access-token",
  accessTokenExpiresAt: "2026-10-06T11:15:00.000Z"
};

describe("apiClient", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("adds the bearer token to authenticated requests", async () => {
    setAccessToken("test-access-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response("", { status: 200 }));

    await apiGet("/api/conversations");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0];
    const headers = new Headers(init?.headers);

    expect(headers.get("Authorization")).toBe("Bearer test-access-token");
    expect(headers.get("Accept")).toBe("application/json");
    expect(init?.credentials).toBe("include");
  });

  it("does not add an authorization header without a token", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response("", { status: 200 }));

    await apiGet("/api/conversations");

    const [, init] = fetchMock.mock.calls[0];
    const headers = new Headers(init?.headers);

    expect(headers.has("Authorization")).toBe(false);
  });

  it("refreshes once and retries a protected request after a 401", async () => {
    setAccessToken("expired-access-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify(refreshPayload), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      )
      .mockResolvedValueOnce(new Response("[]", { status: 200 }));

    const response = await apiGet("/api/conversations");

    expect(response.status).toBe(200);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(String(fetchMock.mock.calls[1][0])).toContain("/api/auth/refresh");
    expect(fetchMock.mock.calls[1][1]?.credentials).toBe("include");

    const [, retryInit] = fetchMock.mock.calls[2];
    const retryHeaders = new Headers(retryInit?.headers);
    expect(retryHeaders.get("Authorization")).toBe(
      "Bearer refreshed-access-token"
    );
  });

  it("clears the client session when refresh reports an expired session", async () => {
    setAccessToken("expired-access-token");
    const unauthorized = vi.fn();
    const unsubscribe = subscribeToUnauthorized(unauthorized);

    vi.spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(new Response("", { status: 401 }))
      .mockResolvedValueOnce(new Response("", { status: 401 }));

    await expect(apiGet("/api/conversations")).rejects.toMatchObject({
      status: 401,
      kind: "unauthorized"
    });

    expect(getAccessToken()).toBeNull();
    expect(unauthorized).toHaveBeenCalledTimes(1);

    unsubscribe();
  });

  it("classifies 429 and parses Retry-After", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("", {
        status: 429,
        headers: { "Retry-After": "7" }
      })
    );

    await expect(apiGet("/api/conversations")).rejects.toMatchObject({
      status: 429,
      kind: "rate_limit",
      retryAfterSeconds: 7
    });
  });

  it("classifies 5xx as server errors", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("", { status: 503 })
    );

    await expect(apiGet("/api/conversations")).rejects.toMatchObject({
      status: 503,
      kind: "server"
    });
  });

  it("supports JSON POST, PATCH and DELETE requests", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response("", { status: 200 }));

    await apiPost("/api/conversations", { title: "Test" });
    await apiPatch("/api/conversations/123", { title: "Updated" });
    await apiDelete("/api/conversations/123");

    expect(fetchMock).toHaveBeenCalledTimes(3);

    const postHeaders = new Headers(fetchMock.mock.calls[0][1]?.headers);
    const patchHeaders = new Headers(fetchMock.mock.calls[1][1]?.headers);

    expect(fetchMock.mock.calls[0][1]?.method).toBe("POST");
    expect(fetchMock.mock.calls[0][1]?.body).toBe(
      JSON.stringify({ title: "Test" })
    );
    expect(postHeaders.get("Content-Type")).toBe("application/json");

    expect(fetchMock.mock.calls[1][1]?.method).toBe("PATCH");
    expect(fetchMock.mock.calls[1][1]?.body).toBe(
      JSON.stringify({ title: "Updated" })
    );
    expect(patchHeaders.get("Content-Type")).toBe("application/json");

    expect(fetchMock.mock.calls[2][1]?.method).toBe("DELETE");
    expect(fetchMock.mock.calls[2][1]?.body).toBeUndefined();
  });

  it("preserves AbortError without wrapping it as a network error", async () => {
    const abortError = new DOMException("Aborted", "AbortError");
    vi.spyOn(globalThis, "fetch").mockRejectedValue(abortError);

    await expect(apiGet("/health")).rejects.toBe(abortError);
  });

  it("rejects an empty access token instead of storing it", () => {
    expect(() => setAccessToken("   ")).toThrow("Access token is required.");
  });
});
