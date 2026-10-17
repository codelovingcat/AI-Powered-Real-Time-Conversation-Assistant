import {
  clearAccessToken,
  getAccessToken,
  getAccessTokenExpiresAt,
  getSessionCookie
} from "./tokenStore";
import {
  AuthRequestError,
  endSession,
  establishSession,
  refreshStoredSession,
  validateCurrentSession,
  type AuthSession
} from "./sessionManager";

export { AuthRequestError, type AuthSession } from "./sessionManager";

export async function signIn(token: string): Promise<AuthSession> {
  // The externally issued token is used only to establish the backend session.
  // Only Conversa's short-lived token and refresh cookie are persisted.
  return establishSession(token);
}

export async function restoreSession(): Promise<AuthSession | null> {
  const accessToken = await getAccessToken();
  const sessionCookie = await getSessionCookie();

  if (!accessToken) {
    return sessionCookie ? refreshStoredSession() : null;
  }

  const expiresAt = await getAccessTokenExpiresAt();
  if (expiresAt && Date.parse(expiresAt) <= Date.now() + 30_000) {
    return refreshStoredSession();
  }

  try {
    return await validateCurrentSession();
  } catch (error: unknown) {
    if (!(error instanceof AuthRequestError) || error.kind !== "unauthorized") {
      // A temporary network/server failure must not destroy a still-usable saved credential.
      throw error;
    }

    await clearAccessToken();
    return refreshStoredSession();
  }
}

export async function signOut(): Promise<boolean> {
  return endSession();
}

/**
 * Maps known failure categories to safe UI text; raw exception/provider messages
 * must not be rendered because they can contain internal response details.
 */
export function getAuthErrorMessage(error: unknown): string {
  if (error instanceof AuthRequestError) {
    switch (error.kind) {
      case "unauthorized":
        return "Your session has expired. Please sign in again.";
      case "network":
        return "The authentication service could not be reached. Check your connection and try again.";
      case "server":
        return "The authentication service is temporarily unavailable. Please try again later.";
      case "client":
        return "Authentication could not be completed. Check the token and try again.";
    }
  }

  return "Authentication could not be completed. Please try again.";
}
