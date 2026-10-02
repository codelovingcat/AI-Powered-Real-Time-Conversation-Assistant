import { useState, type FormEvent } from "react";
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
    } catch {
      // The authentication context exposes a user-safe error message.
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

        {error && (
          <p className="auth-error" role="alert">
            {error}
          </p>
        )}

        <p className="security-note">
          Access tokens are never written to localStorage or sessionStorage.
        </p>
      </section>
    </main>
  );
}
