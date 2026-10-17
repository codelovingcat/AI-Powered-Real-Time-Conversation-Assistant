import * as SecureStore from "expo-secure-store";

const ACCESS_TOKEN_KEY = "conversa.accessToken";
const ACCESS_TOKEN_EXPIRES_AT_KEY = "conversa.accessTokenExpiresAt";
const SESSION_COOKIE_KEY = "conversa.sessionCookie";

const secureOptions = {
  keychainAccessible: SecureStore.WHEN_UNLOCKED
};

export async function getAccessToken(): Promise<string | null> {
  return SecureStore.getItemAsync(ACCESS_TOKEN_KEY);
}

export async function getAccessTokenExpiresAt(): Promise<string | null> {
  return SecureStore.getItemAsync(ACCESS_TOKEN_EXPIRES_AT_KEY);
}

export async function setAccessToken(
  token: string,
  expiresAt?: string
): Promise<void> {
  await SecureStore.setItemAsync(ACCESS_TOKEN_KEY, token, secureOptions);

  if (expiresAt) {
    await SecureStore.setItemAsync(
      ACCESS_TOKEN_EXPIRES_AT_KEY,
      expiresAt,
      secureOptions
    );
  } else {
    await SecureStore.deleteItemAsync(ACCESS_TOKEN_EXPIRES_AT_KEY);
  }
}

export async function getSessionCookie(): Promise<string | null> {
  return SecureStore.getItemAsync(SESSION_COOKIE_KEY);
}

/**
 * Stores only the cookie pair, never the Set-Cookie attributes.
 * The session cookie is a credential and must stay in platform secure storage.
 */
export async function setSessionCookie(cookie: string): Promise<void> {
  if (!cookie.trim()) {
    await SecureStore.deleteItemAsync(SESSION_COOKIE_KEY);
    return;
  }

  await SecureStore.setItemAsync(SESSION_COOKIE_KEY, cookie, secureOptions);
}

export async function clearAccessToken(): Promise<void> {
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS_TOKEN_KEY),
    SecureStore.deleteItemAsync(ACCESS_TOKEN_EXPIRES_AT_KEY)
  ]);
}

export async function clearSessionCredentials(): Promise<void> {
  await Promise.all([
    clearAccessToken(),
    SecureStore.deleteItemAsync(SESSION_COOKIE_KEY)
  ]);
}
