import { afterEach, describe, expect, it } from "vitest";
import { AuthRequestError } from "../services/auth/authSession";
import { getAuthErrorMessage } from "./AuthContext";

afterEach(() => {
  // Keep tests deterministic even if a future helper uses module-local state.
});

describe("getAuthErrorMessage", () => {
  it("maps invalid credentials to a safe, actionable message", () => {
    const error = new AuthRequestError(
      "internal provider detail with secret value",
      401,
      "unauthorized"
    );

    expect(getAuthErrorMessage(error)).toBe(
      "The access token is invalid or expired."
    );
    expect(getAuthErrorMessage(error)).not.toContain("secret");
  });

  it("maps transient service failures without exposing server responses", () => {
    const error = new AuthRequestError(
      "database.internal:5432 password=private",
      503,
      "server"
    );

    expect(getAuthErrorMessage(error)).toBe(
      "The authentication service is temporarily unavailable. Please try again later."
    );
    expect(getAuthErrorMessage(error)).not.toContain("database.internal");
    expect(getAuthErrorMessage(error)).not.toContain("private");
  });

  it("does not expose arbitrary error messages from unexpected failures", () => {
    expect(
      getAuthErrorMessage(new Error("stack trace: signing key material"))
    ).toBe("Authentication could not be completed. Please try again.");
  });

  it("provides a safe fallback for unknown rejection values", () => {
    expect(getAuthErrorMessage({ internal: "credential" })).toBe(
      "Authentication could not be completed. Please try again."
    );
  });
});
