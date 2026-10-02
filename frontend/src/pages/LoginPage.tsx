import { FormEvent, useState } from "react";
import { ApiError } from "../services/api/apiClient";
import { useAuth } from "../auth/AuthContext";

export function LoginPage() {
  const { signIn, state } = useAuth();
  const [token, setToken] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  const error =
    state.status === "unauthenticated" ? state.error : null;

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (!token.trim()) {
      return;
    }

    setIsSubmitting(true);

    try {
      await signIn(token);
      setToken("");
    } catch {
      setToken("");
    } finally {
      setIsSubmitting(false);
    }
  }

  const displayError = error
    ? error instanceof ApiError
      ? getAuthErrorMessage(error)
      : error
    : null;

  return (
    <main className="page-shell">
      <section className="auth-card" aria-labelledby="login-title">
        <p className="eyebrow">CONVERSA WEB</p>
        <h1 id="login-title">Sign in</h1>
        <p className="auth-copy">
          Enter an access token issued by the configured identity system.
          The token is validated by the Conversa API and kept only in memory.
        </p>

        <form className="auth-form" onSubmit={handleSubmit}>
          <label htmlFor="access-token">Access token</label>
          <input
            id="access-token"
            name="access-token"
            type="password"
            value={token}
            onChange={(event) => setToken(event.target.value)}
            autoComplete="off"
            spellCheck={false}
            placeholder="Paste your access token"
            disabled={isSubmitting}
          />

          <button
            className="primary-button"
            type="submit"
            disabled={isSubmitting || !token.trim()}
          >
            {isSubmitting ? "Signing in…" : "Sign in"}
          </button>
        </form>

        {displayError && (
          <p className="auth-error" role="alert">
            {displayError}
          </p>
        )}

        <p className="security-note">
          Access tokens are never written to localStorage or sessionStorage.
        </p>
      </section>
    </main>
  );
}

function getAuthErrorMessage(error: ApiError): string {
  switch (error.kind) {
    case "unauthorized":
      return "The access token is invalid or expired.";
    case "network":
      return "The authentication service could not be reached.";
    case "server":
      return "The authentication service is temporarily unavailable.";
    default:
      return error.message;
  }
}
