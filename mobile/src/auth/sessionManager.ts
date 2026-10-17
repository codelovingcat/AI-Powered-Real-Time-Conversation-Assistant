import { appConfig } from "../config/env";
import {
  clearSessionCredentials,
  getAccessToken,
  getSessionCookie,
  setAccessToken,
  setSessionCookie
} from "./tokenStore";

const SESSION_COOKIE_NAME = "conversa_session";

export interface AuthSession {
  authenticated: true;
  userId: string;
}

interface TokenResponse extends AuthSession {
  accessToken: string;
  accessTokenExpiresAt: string;
}

export type AuthRequestErrorKind =
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

export async function establishSession(
  externalAccessToken: string
): Promise<AuthSession> {
  const normalized = externalAccessToken.trim();
  if (!normalized) {
    throw new AuthRequestError("An access token is required.", 400, "client");
  }

  const response = await authFetch("/api/auth/session", {
    method: "POST",
    headers: {
      Authorization: "Bearer " + normalized
    },
    credentials: "include"
  });

  if (!response.ok) {
    throw await toAuthRequestError(response);
  }

  const tokenResponse = await readTokenResponse(response);
  await storeTokenResponse(tokenResponse);
  await saveSessionCookieFromResponse(response, true);

  return {
    authenticated: true,
    userId: tokenResponse.userId
  };
}

export async function validateCurrentSession(): Promise<AuthSession> {
  const accessToken = await getAccessToken();
  if (!accessToken) {
    throw new AuthRequestError("No saved access token is available.", 401, "unauthorized");
  }

  const response = await authFetch("/api/auth/session", {
    method: "GET",
    headers: {
      Authorization: "Bearer " + accessToken
    },
    credentials: "include"
  });

  if (!response.ok) {
    throw await toAuthRequestError(response);
  }

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new AuthRequestError("The authentication response was invalid.", response.status, "client");
  }

  if (!isAuthSession(payload)) {
    throw new AuthRequestError("The authentication response was invalid.", response.status, "client");
  }

  return payload;
}

/**
 * Refresh is coalesced so simultaneous 401 responses cannot race token replacement.
 * An expired/revoked session clears local credentials; transport/server failures do not.
 */
export function refreshStoredSession(): Promise<AuthSession | null> {
  if (!refreshInFlight) {
    refreshInFlight = performRefresh().finally(() => {
      refreshInFlight = null;
    });
  }

  return refreshInFlight;
}

async function performRefresh(): Promise<AuthSession | null> {
  const headers = new Headers({
    Accept: "application/json"
  });
  const storedCookie = await getSessionCookie();
  if (storedCookie) {
    headers.set("Cookie", storedCookie);
  }

  const response = await authFetch("/api/auth/refresh", {
    method: "POST",
    headers,
    credentials: "include"
  });

  if (response.status === 401) {
    await clearSessionCredentials();
    return null;
  }

  if (!response.ok) {
    throw await toAuthRequestError(response);
  }

  const tokenResponse = await readTokenResponse(response);
  await storeTokenResponse(tokenResponse);
  await saveSessionCookieFromResponse(response);

  return {
    authenticated: true,
    userId: tokenResponse.userId
  };
}

export async function endSession(): Promise<boolean> {
  let serverConfirmed = false;

  try {
    const headers = new Headers({
      Accept: "application/json"
    });
    const storedCookie = await getSessionCookie();
    if (storedCookie) {
      headers.set("Cookie", storedCookie);
    }

    const response = await authFetch("/api/auth/logout", {
      method: "POST",
      headers,
      credentials: "include"
    });
    serverConfirmed = response.ok;
  } catch {
    serverConfirmed = false;
  } finally {
    // Local sign-out is unconditional, even when offline or when the server fails.
    await clearSessionCredentials();
  }

  return serverConfirmed;
}

async function storeTokenResponse(payload: TokenResponse): Promise<void> {
  await setAccessToken(payload.accessToken, payload.accessTokenExpiresAt);
}

async function saveSessionCookieFromResponse(
  response: Response,
  clearWhenUnavailable = false
): Promise<void> {
  // Some native fetch implementations also maintain their own cookie jar and do not
  // expose Set-Cookie to JavaScript. When exposed, keep a secure explicit copy so
  // refresh/logout do not depend solely on native cookie-jar behavior.
  const setCookie = response.headers.get("set-cookie");
  if (!setCookie) {
    if (clearWhenUnavailable) {
      await setSessionCookie("");
    }
    return;
  }

  const cookiePair = setCookie.split(";")[0]?.trim();
  if (cookiePair?.startsWith(SESSION_COOKIE_NAME + "=") && cookiePair.length > SESSION_COOKIE_NAME.length + 1) {
    await setSessionCookie(cookiePair);
  }
}

async function readTokenResponse(response: Response): Promise<TokenResponse> {
  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    throw new AuthRequestError("The authentication response was invalid.", response.status, "client");
  }

  if (
    typeof payload !== "object" ||
    payload === null ||
    !("authenticated" in payload) ||
    payload.authenticated !== true ||
    !("userId" in payload) ||
    typeof payload.userId !== "string" ||
    payload.userId.length === 0 ||
    !("accessToken" in payload) ||
    typeof payload.accessToken !== "string" ||
    payload.accessToken.length === 0 ||
    !("accessTokenExpiresAt" in payload) ||
    typeof payload.accessTokenExpiresAt !== "string" ||
    Number.isNaN(Date.parse(payload.accessTokenExpiresAt))
  ) {
    throw new AuthRequestError("The authentication response was invalid.", response.status, "client");
  }

  return payload as TokenResponse;
}

function isAuthSession(value: unknown): value is AuthSession {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const candidate = value as Record<string, unknown>;
  return candidate.authenticated === true
    && typeof candidate.userId === "string"
    && candidate.userId.length > 0;
}

async function authFetch(path: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(resolveApiUrl(path), {
      ...init,
      headers: new Headers(init.headers)
    });
  } catch {
    throw new AuthRequestError(
      "The authentication service could not be reached.",
      0,
      "network"
    );
  }
}

async function toAuthRequestError(response: Response): Promise<AuthRequestError> {
  if (response.status === 401) {
    return new AuthRequestError(
      "The saved session is invalid or expired.",
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
