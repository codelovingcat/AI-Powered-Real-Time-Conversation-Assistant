let accessToken: string | null = null;

export function setAccessToken(token: string): void {
  const normalized = token.trim();

  if (!normalized) {
    throw new Error("Access token is required.");
  }

  accessToken = normalized;
}

export function getAccessToken(): string | null {
  return accessToken;
}

export function clearAccessToken(): void {
  accessToken = null;
}
