# Web authentication lifecycle UX

The web client keeps the authenticated experience behind `AuthProvider`. During session restoration it announces a checking state and does not briefly render protected screens. An expired session clears the in-memory access token and returns the user to sign-in with a clear, actionable message.

## Sign-in

- The access-token input is required and labelled.
- Empty submissions are blocked by both native form validation and the submit button state.
- The field describes where the token is used, and an authentication error is associated with the field via `aria-describedby`.
- Only fixed, user-safe error messages are rendered. Raw exception messages, provider responses, tokens, claims, and server diagnostics are not displayed.
- The external access token is cleared from the field after a submission attempt and is never stored in browser storage.

## Session expiry and protected UI

A protected API request retries once after attempting to refresh the HttpOnly session cookie. If refresh and retry fail with an unauthorized response, the common API client clears the in-memory access token and notifies the auth context. The authenticated workspace is then replaced by the sign-in page with a session-expired message. Transient network or service errors receive generic actionable copy rather than internal diagnostics.

## Sign-out

The sign-out control shows a pending state while the server logout request runs. The in-memory access token is always cleared. When the server confirms logout, the sign-in page shows confirmation. If the network prevents the server from confirming logout, the user is still signed out locally and receives a warning that the server session could not be confirmed.

## Verification

Browser E2E scenarios cover required-field validation and rejected credentials, successful logout feedback, and an expired-session transition from a protected action to sign-in. Unit tests verify that arbitrary exception text is never surfaced as UI copy.
