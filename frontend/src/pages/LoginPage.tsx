import { useState, type FormEvent } from "react";
import { useAuth } from "../auth/AuthContext";

export function LoginPage() {
  const { signIn, state } = useAuth();
  const [token, setToken] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [dismissedError, setDismissedError] = useState(false);

  const authError =
    state.status === "unauthenticated" ? state.error : null;
  const error = dismissedError ? null : authError;
  const notice =
    state.status === "unauthenticated" ? state.notice : null;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!token.trim() || isSubmitting) {
      return;
    }

    setDismissedError(false);
    setIsSubmitting(true);

    try {
      await signIn(token);
    } catch {
      // AuthContext exposes a fixed, user-safe error message.
    } finally {
      setToken("");
      setIsSubmitting(false);
    }
  }

  return (
    <main className="page-shell">
      <section className="auth-card" aria-labelledby="login-title">
        <p className="eyebrow">CONVERSA WEB</p>
        <h1 id="login-title">Sign in</h1>
        <p className="auth-copy">
          Enter an access token issued by the configured identity system.
          The token is exchanged for a short-lived Conversa access token.
          The long-lived browser session is kept in an HttpOnly cookie.
        </p>

        {notice && (
          <p className="auth-notice" role="status">
            {notice}
          </p>
        )}

        <form
          className="auth-form"
          onSubmit={handleSubmit}
          aria-busy={isSubmitting}
        >
          <label htmlFor="access-token">Access token</label>
          <input
            id="access-token"
            name="access-token"
            type="password"
            value={token}
            onChange={(event) => {
              setToken(event.target.value);
              setDismissedError(true);
            }}
            autoComplete="off"
            spellCheck={false}
            placeholder="Paste your access token"
            aria-describedby="access-token-help"
            aria-invalid={Boolean(error)}
            required
            disabled={isSubmitting}
          />
          <p id="access-token-help" className="form-help">
            The token is used only to sign in and is not saved in browser storage.
          </p>

          <button
            className="primary-button"
            type="submit"
            disabled={isSubmitting || !token.trim()}
          >
            {isSubmitting ? "Signing in…" : "Sign in"}
          </button>
        </form>

        {error && (
          <p className="auth-error" role="alert">
            {error}
          </p>
        )}

        <p className="security-note">
          Access tokens are kept only in memory. The persistent session cookie is
          HttpOnly and is not readable by JavaScript.
        </p>
      </section>
    </main>
  );
}
