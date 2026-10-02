import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "../api/apiClient";
import {
  clearAccessToken,
  getAccessToken
} from "./accessTokenStore";
import {
  signInWithAccessToken,
  signOut,
  validateCurrentSession
} from "./authSession";

describe("authSession", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearAccessToken();
  });

  it("stores an access token only after a successful session validation", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(
          JSON.stringify({
            authenticated: true,
            userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
          }),
          {
            status: 200,
            headers: { "Content-Type": "application/json" }
          }
        )
      );

    const session = await signInWithAccessToken("test-token");

    expect(session.userId).toBe("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    expect(getAccessToken()).toBe("test-token");
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("clears the token when session validation rejects the token", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("", { status: 401 })
    );

    await expect(signInWithAccessToken("expired-token")).rejects.toMatchObject({
      status: 401,
      kind: "unauthorized"
    });

    expect(getAccessToken()).toBeNull();
  });

  it("returns null when no session token exists", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch");

    await expect(validateCurrentSession()).resolves.toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("keeps unexpected session failures visible to the UI", async () => {
    const expected = new ApiError(
      "The service is temporarily unavailable. Please try again later.",
      503,
      "server"
    );

    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response("", { status: 503 })
    );

    // Seed a token so validateCurrentSession performs the request.
    const { setAccessToken } = await import("./accessTokenStore");
    setAccessToken("test-token");

    await expect(validateCurrentSession()).rejects.toEqual(expected);
  });

  it("signs out by clearing the in-memory token", async () => {
    const { setAccessToken } = await import("./accessTokenStore");
    setAccessToken("test-token");

    signOut();

    expect(getAccessToken()).toBeNull();
  });
});
