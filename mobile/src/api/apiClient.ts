import { appConfig } from "../config/env";
import { clearAccessToken, getAccessToken } from "../auth/tokenStore";

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
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");

  if (init.body !== undefined && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const token = await getAccessToken();
  if (token) {
    headers.set("Authorization", "Bearer " + token);
  }

  let response: Response;
  try {
    response = await fetch(resolveApiUrl(path), { ...init, headers });
  } catch {
    throw new ApiError("The API request could not be completed.", 0);
  }

  if (response.ok) {
    return response;
  }

  if (response.status === 401) {
    await clearAccessToken();
  }

  throw new ApiError(
    response.status === 401
      ? "Your session is no longer valid."
      : "The API request was rejected.",
    response.status
  );
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
