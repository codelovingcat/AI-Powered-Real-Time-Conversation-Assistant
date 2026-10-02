import { apiGet } from "../api/apiClient";
import {
  clearAccessToken,
  getAccessToken,
  setAccessToken
} from "./tokenStore";

export interface AuthSession {
  authenticated: true;
  userId: string;
}

function isAuthSession(value: unknown): value is AuthSession {
  if (typeof value !== "object" || value === null) return false;
  const candidate = value as Record<string, unknown>;

  return (
    candidate.authenticated === true &&
    typeof candidate.userId === "string" &&
    candidate.userId.length > 0
  );
}

export async function signIn(token: string): Promise<AuthSession> {
  await setAccessToken(token);
  try {
    return await getCurrentSession();
  } catch (error) {
    await clearAccessToken();
    throw error;
  }
}

export async function restoreSession(): Promise<AuthSession | null> {
  if (!(await getAccessToken())) return null;

  try {
    return await getCurrentSession();
  } catch {
    await clearAccessToken();
    return null;
  }
}

export async function signOut(): Promise<void> {
  await clearAccessToken();
}

async function getCurrentSession(): Promise<AuthSession> {
  const response = await apiGet("/api/auth/session");
  const payload: unknown = await response.json();

  if (!isAuthSession(payload)) {
    throw new Error("The authentication session response is invalid.");
  }

  return payload;
}
