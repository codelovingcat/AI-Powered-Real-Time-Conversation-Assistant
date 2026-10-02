import { appConfig } from "../../config/env";
import {
  clearAccessToken,
  getAccessToken
} from "../auth/accessTokenStore";
import { notifyUnauthorized } from "../auth/authEvents";

export type ApiErrorKind =
  | "unauthorized"
  | "rate_limit"
  | "server"
  | "client"
  | "network";

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly kind: ApiErrorKind,
    public readonly retryAfterSeconds?: number
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export async function apiGet(
  path: string,
  signal?: AbortSignal
): Promise<Response> {
  return apiRequest(path, {
    method: "GET",
    signal
  });
}

export async function apiPost(
  path: string,
  body: unknown,
  signal?: AbortSignal
): Promise<Response> {
  return apiRequest(path, {
    method: "POST",
    body: JSON.stringify(body),
    signal
  });
}

export async function apiPatch(
  path: string,
  body: unknown,
  signal?: AbortSignal
): Promise<Response> {
  return apiRequest(path, {
    method: "PATCH",
    body: JSON.stringify(body),
    signal
  });
}

export async function apiDelete(
  path: string,
  signal?: AbortSignal
): Promise<Response> {
  return apiRequest(path, {
    method: "DELETE",
    signal
  });
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

  const token = getAccessToken();
  if (token) {
    headers.set("Authorization", "Bearer " + token);
  }

  let response: Response;

  try {
    response = await fetch(resolveApiUrl(path), {
      ...init,
      headers,
      credentials: "same-origin"
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw error;
    }

    throw new ApiError(
      "The API request could not be completed.",
      0,
      "network"
    );
  }

  if (response.ok) {
    return response;
  }

  throw createApiError(response);
}

function createApiError(response: Response): ApiError {
  if (response.status === 401) {
    clearAccessToken();
    notifyUnauthorized();

    return new ApiError(
      "Your session is no longer valid.",
      response.status,
      "unauthorized"
    );
  }

  if (response.status === 429) {
    return new ApiError(
      "Too many requests. Please try again later.",
      response.status,
      "rate_limit",
      parseRetryAfter(response.headers.get("Retry-After"))
    );
  }

  if (response.status >= 500) {
    return new ApiError(
      "The service is temporarily unavailable. Please try again later.",
      response.status,
      "server"
    );
  }

  return new ApiError(
    "The API request was rejected.",
    response.status,
    "client"
  );
}

function parseRetryAfter(value: string | null): number | undefined {
  if (!value) {
    return undefined;
  }

  const seconds = Number(value);
  if (Number.isFinite(seconds) && seconds >= 0) {
    return Math.ceil(seconds);
  }

  const date = Date.parse(value);
  if (Number.isNaN(date)) {
    return undefined;
  }

  const delay = Math.ceil((date - Date.now()) / 1000);
  return delay > 0 ? delay : 0;
}

function resolveApiUrl(path: string): string {
  if (!appConfig.apiBaseUrl) {
    return path;
  }

  return appConfig.apiBaseUrl + (path.startsWith("/") ? path : "/" + path);
}
