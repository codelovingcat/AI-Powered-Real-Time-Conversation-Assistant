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

import {
  AuthRequestError,
  restoreSession,
  signIn,
  signOut
} from "./authService";
import {
  getAccessToken,
  getAccessTokenExpiresAt,
  getSessionCookie,
  setAccessToken,
  setSessionCookie
} from "./tokenStore";

function jsonResponse(
  payload: unknown,
  status = 200,
  headers: Record<string, string> = {}
): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      "Content-Type": "application/json",
      ...headers
    }
  });
}

function tokenResponse(accessToken: string, userId = "user-123") {
  return {
    authenticated: true,
    userId,
    accessToken,
    accessTokenExpiresAt: "2099-01-01T00:00:00Z"
  };
}

describe("mobile authenticated session", () => {
  beforeEach(() => {
    secureValues.clear();
    vi.stubGlobal("fetch", vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
    secureValues.clear();
  });

  it("exchanges the identity token and stores only the Conversa session credentials", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(
      jsonResponse(tokenResponse("conversa-short-lived-token"), 200, {
        "Set-Cookie": "conversa_session=refresh-secret; Path=/api/auth; HttpOnly; Secure; SameSite=None"
      })
    );

    const session = await signIn("external-identity-token");

    expect(session).toEqual({ authenticated: true, userId: "user-123" });
    expect(fetch).toHaveBeenCalledTimes(1);
    const [url, init] = vi.mocked(fetch).mock.calls[0]!;
    expect(String(url)).toContain("/api/auth/session");
    expect(init?.method).toBe("POST");
    expect(new Headers(init?.headers).get("Authorization")).toBe("Bearer external-identity-token");
    expect(await getAccessToken()).toBe("conversa-short-lived-token");
    expect(await getAccessTokenExpiresAt()).toBe("2099-01-01T00:00:00Z");
    expect(await getSessionCookie()).toBe("conversa_session=refresh-secret");
    expect(await getAccessToken()).not.toBe("external-identity-token");
  });

  it("refreshes an expired saved access token using the secure session cookie", async () => {
    await setAccessToken("expired-access-token", "2020-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=refresh-secret");
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(tokenResponse("new-access-token")));

    const session = await restoreSession();

    expect(session).toEqual({ authenticated: true, userId: "user-123" });
    expect(fetch).toHaveBeenCalledTimes(1);
    const [url, init] = vi.mocked(fetch).mock.calls[0]!;
    expect(String(url)).toContain("/api/auth/refresh");
    expect(new Headers(init?.headers).get("Cookie")).toBe("conversa_session=refresh-secret");
    expect(await getAccessToken()).toBe("new-access-token");
    expect(await getAccessTokenExpiresAt()).toBe("2099-01-01T00:00:00Z");
  });

  it("preserves credentials when restoring a session fails due to a network problem", async () => {
    await setAccessToken("still-valid-token", "2099-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=refresh-secret");
    vi.mocked(fetch).mockRejectedValueOnce(new Error("offline"));

    await expect(restoreSession()).rejects.toMatchObject({
      name: "AuthRequestError",
      kind: "network"
    } satisfies Partial<AuthRequestError>);

    expect(await getAccessToken()).toBe("still-valid-token");
    expect(await getSessionCookie()).toBe("conversa_session=refresh-secret");
  });

  it("clears all local credentials when signing out cannot reach the server", async () => {
    await setAccessToken("saved-token", "2099-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=refresh-secret");
    vi.mocked(fetch).mockRejectedValueOnce(new Error("offline"));

    await expect(signOut()).resolves.toBe(false);

    expect(await getAccessToken()).toBeNull();
    expect(await getAccessTokenExpiresAt()).toBeNull();
    expect(await getSessionCookie()).toBeNull();
  });

  it("clears credentials when the saved refresh session is expired", async () => {
    await setAccessToken("expired-access-token", "2020-01-01T00:00:00Z");
    await setSessionCookie("conversa_session=expired-refresh-secret");
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ title: "Unauthorized" }, 401));

    await expect(restoreSession()).resolves.toBeNull();

    expect(await getAccessToken()).toBeNull();
    expect(await getSessionCookie()).toBeNull();
  });
});
