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
    }
  | {
      status: "authenticated";
      session: AuthSession;
      error: null;
    }
  | {
      status: "unauthenticated";
      session: null;
      error: string | null;
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
    error: null
  });

  useEffect(() => {
    return subscribeToUnauthorized(() => {
      setState({
        status: "unauthenticated",
        session: null,
        error: "Your session has expired. Please sign in again."
      });
    });
  }, []);

  useEffect(() => {
    const controller = new AbortController();

    void validateCurrentSession(controller.signal)
      .then((session) => {
        if (controller.signal.aborted) {
          return;
        }

        setState(
          session
            ? {
                status: "authenticated",
                session,
                error: null
              }
            : {
                status: "unauthenticated",
                session: null,
                error: null
              }
        );
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setState({
          status: "unauthenticated",
          session: null,
          error: getAuthErrorMessage(error)
        });
      });

    return () => controller.abort();
  }, []);

  const signIn = useCallback(async (token: string) => {
    setState({
      status: "checking",
      session: null,
      error: null
    });

    try {
      const session = await signInWithAccessToken(token);
      setState({
        status: "authenticated",
        session,
        error: null
      });
    } catch (error: unknown) {
      setState({
        status: "unauthenticated",
        session: null,
        error: getAuthErrorMessage(error)
      });

      throw error;
    }
  }, []);

  const signOut = useCallback(async () => {
    try {
      await clearSession();
    } finally {
      setState({
        status: "unauthenticated",
        session: null,
        error: null
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

function getAuthErrorMessage(error: unknown): string {
  if (error instanceof AuthRequestError) {
    return error.message;
  }

  return error instanceof Error
    ? error.message
    : "Authentication could not be completed.";
}
