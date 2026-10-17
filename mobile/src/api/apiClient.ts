import { appConfig } from "../config/env";
import { notifySessionExpired } from "../auth/authEvents";
import { refreshStoredSession, type AuthSession } from "../auth/sessionManager";
import {
  clearSessionCredentials,
  getAccessToken
} from "../auth/tokenStore";

export class ApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = "ApiError";
  }
}

export async function apiRequest(
  path: string,
  init: RequestInit = {}
): Promise<Response> {
  const response = await sendRequest(path, init);

  if (response.status !== 401) {
    if (response.ok) {
      return response;
    }

    throw new ApiError("The API request was rejected.", response.status);
  }

  let refreshedSession: AuthSession | null;
  try {
    refreshedSession = await refreshStoredSession();
  } catch {
    // A transient refresh failure should not erase saved credentials or falsely
    // claim that the session is invalid; let the caller retry the operation.
    throw new ApiError("The API request could not be completed. Check your connection and try again.", 0);
  }

  if (refreshedSession) {
    const retry = await sendRequest(path, init);
    if (retry.ok) {
      return retry;
    }

    if (retry.status !== 401) {
      throw new ApiError("The API request was rejected.", retry.status);
    }
  }

  await clearSessionCredentials();
  notifySessionExpired();
  throw new ApiError("Your session has expired. Please sign in again.", 401);
}

async function sendRequest(path: string, init: RequestInit): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");

  if (init.body !== undefined && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  // Always read the latest secure token so a retry uses a newly refreshed token.
  headers.delete("Authorization");
  const token = await getAccessToken();
  if (token) {
    headers.set("Authorization", "Bearer " + token);
  }

  try {
    return await fetch(resolveApiUrl(path), { ...init, headers });
  } catch {
    throw new ApiError("The API request could not be completed. Check your connection and try again.", 0);
  }
}

export async function apiGet(path: string): Promise<Response> {
  return apiRequest(path, { method: "GET" });
}

export async function apiPost(path: string, body: unknown): Promise<Response> {
  return apiRequest(path, { method: "POST", body: JSON.stringify(body) });
}

function resolveApiUrl(path: string): string {
  if (!appConfig.apiBaseUrl) {
    return path;
  }

  return appConfig.apiBaseUrl + (path.startsWith("/") ? path : "/" + path);
}
