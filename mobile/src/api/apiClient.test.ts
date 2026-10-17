import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const secureValues = vi.hoisted(() => new Map<string, string>());

vi.mock("expo-secure-store", () => ({
  WHEN_UNLOCKED: "WHEN_UNLOCKED",
  getItemAsync: vi.fn(async (key: string) => secureValues.get(key) ?? null),
  setItemAsync: vi.fn(async (key: string, value: string) => {
    secureValues.set(key, value);
  }),
  deleteItemAsync: vi.fn(async (key: string) => {
    secureValues.delete(key);
  })
}));

import { apiGet, ApiError } from "./apiClient";
import { subscribeToSessionExpired } from "../auth/authEvents";
import { getAccessToken, getSessionCookie, setAccessToken, setSessionCookie } from "../auth/tokenStore";

function jsonResponse(payload: unknown, status = 200): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

function tokenResponse(accessToken: string) {
  return {
    authenticated: true,
    userId: "user-123",
    accessToken,
    accessTokenExpiresAt: "2099-01-01T00:00:00Z"
  };
}

describe("mobile API authentication", () => {
  beforeEach(() => {
    secureValues.clear();
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
    secureValues.clear();
  });

  it("refreshes after 401 and retries the request exactly once with the new token", async () => {
    await setAccessToken("old-access-token", "2099-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=refresh-secret");
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: "Unauthorized" }, 401))
      .mockResolvedValueOnce(jsonResponse(tokenResponse("new-access-token")))
      .mockResolvedValueOnce(jsonResponse({ items: [], nextCursor: null }));

    const response = await apiGet("/api/conversations");

    expect(response.status).toBe(200);
    expect(fetch).toHaveBeenCalledTimes(3);
    const [, refreshInit] = vi.mocked(fetch).mock.calls[1]!;
    expect(String(vi.mocked(fetch).mock.calls[1]![0])).toContain("/api/auth/refresh");
    expect(new Headers(refreshInit?.headers).get("Cookie")).toBe("conversa_session=refresh-secret");
    const [, retryInit] = vi.mocked(fetch).mock.calls[2]!;
    expect(new Headers(retryInit?.headers).get("Authorization")).toBe("Bearer new-access-token");
    expect(await getAccessToken()).toBe("new-access-token");
  });

  it("clears credentials and notifies the app when the refresh session has expired", async () => {
    await setAccessToken("old-access-token", "2099-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=expired-refresh-secret");
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: "Unauthorized" }, 401))
      .mockResolvedValueOnce(jsonResponse({ title: "Unauthorized" }, 401));

    let expired = 0;
    const unsubscribe = subscribeToSessionExpired(() => {
      expired += 1;
    });

    try {
      await expect(apiGet("/api/conversations")).rejects.toMatchObject({
        name: "ApiError",
        status: 401
      } satisfies Partial<ApiError>);
    } finally {
      unsubscribe();
    }

    expect(expired).toBe(1);
    expect(await getAccessToken()).toBeNull();
    expect(await getSessionCookie()).toBeNull();
  });

  it("does not erase credentials on a transient refresh failure", async () => {
    await setAccessToken("old-access-token", "2099-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=refresh-secret");
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse({ title: "Unauthorized" }, 401))
      .mockRejectedValueOnce(new Error("offline"));

    await expect(apiGet("/api/conversations")).rejects.toMatchObject({
      name: "ApiError",
      status: 0
    } satisfies Partial<ApiError>);

    expect(await getAccessToken()).toBe("old-access-token");
    expect(await getSessionCookie()).toBe("conversa_session=refresh-secret");
  });
});
