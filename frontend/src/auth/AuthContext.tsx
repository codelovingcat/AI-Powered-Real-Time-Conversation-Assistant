import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode
} from "react";
import { AuthRequestError } from "../services/auth/authSession";
import { subscribeToUnauthorized } from "../services/auth/authEvents";
import {
  signInWithAccessToken,
  signOut as clearSession,
  validateCurrentSession,
  type AuthSession
} from "../services/auth/authSession";

type AuthState =
  | {
      status: "checking";
      session: null;
      error: null;
      notice: null;
    }
  | {
      status: "authenticated";
      session: AuthSession;
      error: null;
      notice: null;
    }
  | {
      status: "unauthenticated";
      session: null;
      error: string | null;
      notice: string | null;
    };

interface AuthContextValue {
  state: AuthState;
  signIn: (token: string) => Promise<void>;
  signOut: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({
    status: "checking",
    session: null,
    error: null,
    notice: null
  });

  useEffect(() => {
    return subscribeToUnauthorized(() => {
      setState({
        status: "unauthenticated",
        session: null,
        error: "Your session has expired. Please sign in again.",
        notice: null
      });
    });
  }, []);

  useEffect(() => {
    // Do not bind the shared refresh request to this effect's AbortSignal.
    // React StrictMode can replay this effect: aborting the first request
    // would also abort the in-flight promise reused by the second setup.
    let isCurrent = true;

    void validateCurrentSession()
      .then((session) => {
        if (!isCurrent) {
          return;
        }

        setState(
          session
            ? {
                status: "authenticated",
                session,
                error: null,
                notice: null
              }
            : {
                status: "unauthenticated",
                session: null,
                error: null,
                notice: null
              }
        );
      })
      .catch((error: unknown) => {
        if (!isCurrent) {
          return;
        }

        setState({
          status: "unauthenticated",
          session: null,
          error: getAuthErrorMessage(error),
          notice: null
        });
      });

    return () => {
      isCurrent = false;
    };
  }, []);

  const signIn = useCallback(async (token: string) => {
    setState({
      status: "checking",
      session: null,
      error: null,
      notice: null
    });

    try {
      const session = await signInWithAccessToken(token);
      setState({
        status: "authenticated",
        session,
        error: null,
        notice: null
      });
    } catch (error: unknown) {
      setState({
        status: "unauthenticated",
        session: null,
        error: getAuthErrorMessage(error),
        notice: null
      });

      throw error;
    }
  }, []);

  const signOut = useCallback(async () => {
    let notice = "You have signed out successfully.";

    try {
      await clearSession();
    } catch {
      // The local access token is cleared even when the network request fails.
      // Do not expose raw network/provider errors to the UI.
      notice =
        "You have been signed out on this device, but the server could not confirm logout. Check your connection before leaving this device.";
    } finally {
      setState({
        status: "unauthenticated",
        session: null,
        error: null,
        notice
      });
    }
  }, []);

  const value = useMemo(
    () => ({ state, signIn, signOut }),
    [state, signIn, signOut]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);

  if (!value) {
    throw new Error("useAuth must be used within an AuthProvider.");
  }

  return value;
}

/**
 * Maps only known authentication failure categories to UI-safe copy.
 * Error.message is never rendered because it can contain internal details.
 */
export function getAuthErrorMessage(error: unknown): string {
  if (error instanceof AuthRequestError) {
    switch (error.kind) {
      case "unauthorized":
        return "The access token is invalid or expired.";
      case "network":
        return "The authentication service could not be reached. Check your connection and try again.";
      case "server":
        return "The authentication service is temporarily unavailable. Please try again later.";
      case "client":
        return "Authentication could not be completed. Check the token and try again.";
    }
  }

  return "Authentication could not be completed. Please try again.";
}
