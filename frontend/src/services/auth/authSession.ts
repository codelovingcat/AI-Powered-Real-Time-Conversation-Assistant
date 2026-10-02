import { ApiError, apiGet } from "../api/apiClient";
import {
  clearAccessToken,
  getAccessToken,
  setAccessToken
} from "./accessTokenStore";

export interface AuthSession {
  authenticated: true;
  userId: string;
}

function isAuthSession(value: unknown): value is AuthSession {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;

  return (
    candidate.authenticated === true &&
    typeof candidate.userId === "string" &&
    candidate.userId.length > 0
  );
}

export async function signInWithAccessToken(
  token: string,
  signal?: AbortSignal
): Promise<AuthSession> {
  setAccessToken(token);

  try {
    return await fetchCurrentSession(signal);
  } catch (error) {
    clearAccessToken();
    throw error;
  }
}

export async function validateCurrentSession(
  signal?: AbortSignal
): Promise<AuthSession | null> {
  if (!getAccessToken()) {
    return null;
  }

  try {
    return await fetchCurrentSession(signal);
  } catch (error) {
    if (error instanceof ApiError && error.kind === "unauthorized") {
      clearAccessToken();
      return null;
    }

    throw error;
  }
}

export function signOut(): void {
  clearAccessToken();
}

async function fetchCurrentSession(
  signal?: AbortSignal
): Promise<AuthSession> {
  const response = await apiGet("/api/auth/session", signal);
  const payload: unknown = await response.json();

  if (!isAuthSession(payload)) {
    throw new Error("The authentication session response is invalid.");
  }

  return payload;
}
