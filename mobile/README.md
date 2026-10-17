# Conversa Mobile

React Native/Expo foundation for the Conversa client.

## Scope

The mobile client currently includes:
- React Native + Expo application shell
- Secure storage for Conversa's short-lived access token and the persistent session cookie
- Startup session validation and refresh
- Sign-in by exchanging an externally issued identity access token with `POST /api/auth/session`
- Expiry/logout handling and one refresh-and-retry attempt for protected API requests
- Shared backend API base URL through `EXPO_PUBLIC_API_BASE_URL`
- Conversation list and creation using the existing backend contract
- Initial conversation screen

Native microphone/audio streaming is intentionally left for a later mobile audio task.

## Authentication lifecycle

1. The user enters an externally issued identity token. The app sends it only to `POST /api/auth/session`; it does not persist that external token.
2. The backend returns a short-lived Conversa access token and sets the `conversa_session` HttpOnly session cookie. The access token, expiry timestamp and an explicitly available cookie pair are stored with Expo SecureStore.
3. On startup, the app validates the saved access token. If it is expired or rejected, the app attempts `POST /api/auth/refresh` and saves the new short-lived token.
4. A protected API request that receives HTTP 401 triggers one coalesced refresh attempt and retries the original request once.
5. If the refresh session is invalid or expired, local credentials are deleted and the app returns to sign-in. Temporary network/server errors do not silently erase credentials and can be retried.
6. Sign-out calls `POST /api/auth/logout` and clears local credentials even if the server cannot be reached.

The session cookie is sent using the native fetch cookie mechanism and, where native fetch exposes `Set-Cookie`, a secure-store copy of just the cookie pair. No credential is written to AsyncStorage, logs or `EXPO_PUBLIC_*` configuration.

## Commands

From this directory:

    npm install
    npm run typecheck
    npm test
    npm start

## Security

- Do not commit access tokens, session cookies, API keys, or provider credentials.
- `EXPO_PUBLIC_*` values are bundled into the application and must never contain secrets.
- Authentication credentials are stored with Expo SecureStore, not AsyncStorage or source code.
- Errors shown in the UI are mapped to fixed, user-safe messages; raw provider or server response bodies are not rendered.
