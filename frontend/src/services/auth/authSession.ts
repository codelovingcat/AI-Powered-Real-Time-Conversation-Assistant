import { appConfig } from "../../config/env";
import {
  clearAccessToken,
  setAccessToken
} from "./accessTokenStore";

export interface AuthSession {
  authenticated: true;
  userId: string;
}

interface TokenResponse {
  authenticated: true;
  userId: string;
  accessToken: string;
  accessTokenExpiresAt: string;
}

type AuthRequestErrorKind =
  | "unauthorized"
  | "server"
  | "client"
  | "network";

export class AuthRequestError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly kind: AuthRequestErrorKind
  ) {
    super(message);
    this.name = "AuthRequestError";
  }
}

let refreshInFlight: Promise<AuthSession | null> | null = null;

function isAuthSession(value: unknown): value is AuthSession {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as AuthSession & Record<string, unknown>;

  return (
    candidate.authenticated === true &&
    typeof candidate.userId === "string" &&
    candidate.userId.length > 0
  );
}

function isTokenResponse(value: unknown): value is TokenResponse {
  if (!isAuthSession(value)) {
    return false;
  }

  const candidate = value as AuthSession & Record<string, unknown>;

  return (
    typeof candidate.accessToken === "string" &&
    candidate.accessToken.length > 0 &&
    typeof candidate.accessTokenExpiresAt === "string" &&
    !Number.isNaN(Date.parse(candidate.accessTokenExpiresAt))
  );
}

export async function signInWithAccessToken(
  token: string,
  signal?: AbortSignal
): Promise<AuthSession> {
  const normalized = token.trim();

  if (!normalized) {
    throw new AuthRequestError("Access token is required.", 400, "client");
  }

  try {
    const response = await authRequest("/api/auth/session", {
      method: "POST",
      headers: {
        Authorization: "Bearer " + normalized
      },
      signal
    });

    const payload: unknown = await response.json();
    if (!isTokenResponse(payload)) {
      throw new Error("The authentication session response is invalid.");
    }

    setAccessToken(payload.accessToken);

    return {
      authenticated: true,
      userId: payload.userId
    };
  } catch (error) {
    clearAccessToken();
    throw error;
  }
}

export function validateCurrentSession(
  signal?: AbortSignal
): Promise<AuthSession | null> {
  return refreshCurrentSession(signal);
}

export function refreshCurrentSession(
  signal?: AbortSignal
): Promise<AuthSession | null> {
  if (!refreshInFlight) {
    refreshInFlight = requestRefresh(signal).finally(() => {
      refreshInFlight = null;
    });
  }

  return refreshInFlight;
}

export async function signOut(): Promise<void> {
  try {
    await authRequest("/api/auth/logout", {
      method: "POST"
    });
  } finally {
    clearAccessToken();
  }
}

async function requestRefresh(
  signal?: AbortSignal
): Promise<AuthSession | null> {
  let response: Response;

  try {
    response = await fetch(resolveApiUrl("/api/auth/refresh"), {
      method: "POST",
      headers: {
        Accept: "application/json"
      },
      credentials: "include",
      signal
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw error;
    }

    throw new AuthRequestError(
      "The authentication service could not be reached.",
      0,
      "network"
    );
  }

  if (response.status === 401) {
    clearAccessToken();
    return null;
  }

  if (!response.ok) {
    throw await createAuthRequestError(response);
  }

  const payload: unknown = await response.json();
  if (!isTokenResponse(payload)) {
    throw new Error("The authentication refresh response is invalid.");
  }

  setAccessToken(payload.accessToken);

  return {
    authenticated: true,
    userId: payload.userId
  };
}

async function authRequest(
  path: string,
  init: RequestInit
): Promise<Response> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");

  let response: Response;

  try {
    response = await fetch(resolveApiUrl(path), {
      ...init,
      headers,
      credentials: "include"
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw error;
    }

    throw new AuthRequestError(
      "The authentication service could not be reached.",
      0,
      "network"
    );
  }

  if (!response.ok) {
    throw await createAuthRequestError(response);
  }

  return response;
}

async function createAuthRequestError(
  response: Response
): Promise<AuthRequestError> {
  if (response.status === 401) {
    return new AuthRequestError(
      "The access token is invalid or expired.",
      response.status,
      "unauthorized"
    );
  }

  if (response.status >= 500) {
    return new AuthRequestError(
      "The authentication service is temporarily unavailable.",
      response.status,
      "server"
    );
  }

  return new AuthRequestError(
    "Authentication could not be completed.",
    response.status,
    "client"
  );
}

function resolveApiUrl(path: string): string {
  if (!appConfig.apiBaseUrl) {
    return path;
  }

  return appConfig.apiBaseUrl + (path.startsWith("/") ? path : "/" + path);
}
