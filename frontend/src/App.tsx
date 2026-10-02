import { AuthProvider, useAuth } from "./auth/AuthContext";
import { LoginPage } from "./pages/LoginPage";
import { HomePage } from "./pages/HomePage";
import "./styles.css";

export default function App() {
  return (
    <AuthProvider>
      <AuthenticatedApp />
    </AuthProvider>
  );
}

function AuthenticatedApp() {
  const { state } = useAuth();

  if (state.status === "checking") {
    return (
      <main className="page-shell">
        <section className="auth-card" aria-live="polite">
          <p className="eyebrow">CONVERSA WEB</p>
          <h1>Checking session</h1>
          <p className="auth-copy">
            Verifying your current authentication session.
          </p>
        </section>
      </main>
    );
  }

  return state.status === "authenticated" ? <HomePage /> : <LoginPage />;
}
