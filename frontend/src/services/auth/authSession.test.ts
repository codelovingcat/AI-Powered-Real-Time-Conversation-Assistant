import { afterEach, describe, expect, it, vi } from "vitest";
import {
  clearAccessToken,
  getAccessToken,
  setAccessToken
} from "./accessTokenStore";
import {
  signInWithAccessToken,
  signOut,
  validateCurrentSession
} from "./authSession";

const refreshedPayload = {
  authenticated: true,
  userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  accessToken: "short-lived-access-token",
  accessTokenExpiresAt: "2026-10-06T11:15:00.000Z"
};

describe("authSession", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("exchanges the external access token for a short-lived app token", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify(refreshedPayload), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    const session = await signInWithAccessToken(" external-token ");

    expect(session).toEqual({
      authenticated: true,
      userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    });
    expect(getAccessToken()).toBe("short-lived-access-token");

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/auth/session");
    expect(init?.method).toBe("POST");
    expect(new Headers(init?.headers).get("Authorization")).toBe(
      "Bearer external-token"
    );
    expect(init?.credentials).toBe("include");
  });

  it("restores a session after reload using the persistent cookie", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify(refreshedPayload), {
          status: 200,
          headers: { "Content-Type": "application/json" }
        })
      );

    const session = await validateCurrentSession();

    expect(session).toEqual({
      authenticated: true,
      userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    });
    expect(getAccessToken()).toBe("short-lived-access-token");

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/auth/refresh");
    expect(init?.method).toBe("POST");
    expect(init?.credentials).toBe("include");
    expect(new Headers(init?.headers).has("Authorization")).toBe(false);
  });

  it("returns null and clears the access token when the persistent session is expired", async () => {
    setAccessToken("expired-access-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response("", { status: 401 }));

    await expect(validateCurrentSession()).resolves.toBeNull();

    expect(getAccessToken()).toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("shares concurrent refresh requests", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify(refreshedPayload), {
        status: 200,
        headers: { "Content-Type": "application/json" }
      })
    );

    const [first, second] = await Promise.all([
      validateCurrentSession(),
      validateCurrentSession()
    ]);

    expect(first?.userId).toBe(second?.userId);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("logs out remotely and clears the local access token", async () => {
    setAccessToken("short-lived-access-token");

    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(new Response(null, { status: 204 }));

    await signOut();

    expect(getAccessToken()).toBeNull();

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/auth/logout");
    expect(init?.method).toBe("POST");
    expect(init?.credentials).toBe("include");
  });

  it("clears the local access token when logout fails", async () => {
    setAccessToken("short-lived-access-token");

    vi.spyOn(globalThis, "fetch").mockRejectedValue(
      new Error("network failure")
    );

    await expect(signOut()).rejects.toMatchObject({
      status: 0,
      kind: "network"
    });
    expect(getAccessToken()).toBeNull();
  });
});
