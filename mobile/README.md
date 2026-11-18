# Conversa Mobile

React Native / Expo client for authenticated, real-time conversations.

## Implemented mobile flows

- Secure sign-in using an externally issued identity access token.
- Short-lived Conversa access token and session cookie stored with Expo SecureStore.
- Startup session restoration, refresh and sign-out.
- Conversation creation, selection and paginated history endpoint integration.
- Live microphone capture and WebSocket audio streaming.
- Partial/final transcript timeline and automatic AI assistance for final transcript segments.
- Pause, resume, finish and bounded reconnect handling.
- Provider-neutral error messages and accessible recording controls.

## Saved conversation history

The **History** screen uses the same authenticated conversation endpoints and server-side ownership checks as the web client.

- Conversation list pages use `GET /api/conversations?limit=30&cursor=...`.
- Search is sent to the server and can be combined with UTC update-date bounds (`updatedFrom` inclusive, `updatedTo` exclusive).
- The **Load more conversations** action requests the next page using the returned opaque cursor. The app does not download the full history or locally filter an incomplete page.
- Opening a saved record loads its metadata from `GET /api/conversations/{id}` and the newest message page from `GET /api/conversations/{id}/messages?limit=30`.
- Older messages are fetched incrementally with the message cursor and prepended in chronological order.
- Pull-to-refresh reloads the first list page. Returning from a detail screen remounts the list and re-reads server state.
- A deleted or no-longer-authorized conversation is reported as unavailable. Deleting from the detail screen calls the owner-scoped `DELETE /api/conversations/{id}` endpoint; on success the record is removed from the mobile list immediately.

Conversation IDs and cursors are used only as resource/position references. The API independently enforces the authenticated user's ownership on list, detail, message and delete operations. Client-side filtering is a convenience only; it is not an authorization boundary.

History reads and detail reads are cancellable when screens change, so stale responses do not overwrite a newly selected conversation. Search, load-more and refresh errors have retry states; empty search results are distinguished from an account with no saved conversations.

## Requirements

- Node.js compatible with Expo SDK 54.
- Android Studio / Android SDK for Android builds, or Xcode for iOS builds.
- A reachable Conversa API URL.
- A native development build. **Expo Go is not supported** because the realtime microphone module includes native code.

## Configuration

Copy `.env.example` to `.env` and set the API address:

    EXPO_PUBLIC_API_BASE_URL=http://localhost:5080

For a physical phone, use the API host's LAN address or an HTTPS deployment that the phone can reach. On Android Emulator, use `http://10.0.2.2:5080` to reach the host machine. Use HTTPS/WSS for non-local environments.

The API address is public application configuration. Never put access tokens, refresh cookies, Gemini keys, Deepgram keys or other secrets in `EXPO_PUBLIC_*` variables.

## Native audio and wire format

The mobile app uses `react-native-audio-api` data callbacks. It captures mono microphone frames, converts floating-point samples to signed 16-bit little-endian PCM, and streams binary chunks using:

- Content type: `audio/raw`
- Encoding: Linear16 / PCM16
- Sample rate: 16,000 Hz
- Channels: 1 (mono)
- Callback batch: approximately 100 ms

No local audio file is created. The microphone permission is requested only after the user explicitly starts a conversation. Audio capture and the WebSocket are stopped when the user pauses/finishes or the app moves to the background. Returning to the foreground does not restart the microphone automatically.

The WebSocket endpoint is:

    /ws/conversations/{conversationId}/audio

The short-lived Conversa access token is sent as the `conversa.auth` WebSocket subprotocol, not in the URL or query string. The mobile app never receives or stores a speech-provider credential. The client also caps its outbound WebSocket buffer and stops streaming rather than allowing unbounded audio to accumulate.

## Run locally

From this directory:

    npm install
    npx expo prebuild
    npm run android

For iOS on macOS:

    npm install
    npx expo prebuild
    npm run ios

After installing the native development client, start Metro with:

    npm start

When native dependencies or app plugins change, regenerate/rebuild the native application.

## Validation

    npm run typecheck
    npm test

The PCM conversion tests run without a native device. Microphone permission, actual device audio delivery, Bluetooth routing and live provider behaviour still require a native build and physical/simulator testing with the API and STT provider configured.

## Authentication lifecycle

1. The user enters an externally issued identity token. The app sends it only to `POST /api/auth/session`; it does not persist that external token.
2. The backend returns a short-lived Conversa access token and sets the `conversa_session` HttpOnly session cookie. The app stores the token, expiry and an explicitly available cookie pair with Expo SecureStore.
3. On startup, the app validates the saved access token. If expired or rejected, it attempts `POST /api/auth/refresh`.
4. A protected API request that receives HTTP 401 triggers one coalesced refresh attempt and retries the original request once.
5. An invalid or expired refresh session clears local credentials. Temporary network/server failures do not silently erase credentials.
6. Sign-out calls `POST /api/auth/logout` and clears local credentials even if the server cannot be reached.

The session cookie is sent using the native fetch cookie mechanism and, where native fetch exposes `Set-Cookie`, a secure-store copy of only the cookie pair. Credentials are not written to AsyncStorage or application logs.

## Security

- Never commit access tokens, session cookies, provider credentials or personal conversation transcripts.
- `EXPO_PUBLIC_*` values are bundled into the application and must never contain secrets.
- Authentication credentials remain in Expo SecureStore.
- Raw provider messages, provider diagnostics and internal identifiers are not rendered as user-facing errors.
