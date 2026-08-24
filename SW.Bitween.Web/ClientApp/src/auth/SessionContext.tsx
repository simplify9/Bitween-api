import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";
import { api, type PermissionKey, type Session } from "../api";

interface SessionContextValue {
  session: Session | null;
  /** True until the stored session has been checked once at startup. */
  initializing: boolean;
  can: (permission: PermissionKey) => boolean;
  signIn: (email: string, password: string) => Promise<Session>;
  signInWithMicrosoft: () => Promise<Session>;
  /** Adopt a session produced elsewhere (invite acceptance, demo switch). */
  adoptSession: (session: Session) => void;
  /** Re-fetch the session after profile changes. */
  refresh: () => Promise<void>;
  signOut: () => Promise<void>;
}

const SessionContext = createContext<SessionContextValue | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(null);
  const [initializing, setInitializing] = useState(true);
  const queryClient = useQueryClient();

  useEffect(() => {
    api
      .getSession()
      .then(setSession)
      .finally(() => setInitializing(false));
  }, []);

  const adoptSession = useCallback(
    (next: Session) => {
      // a different identity invalidates everything previously fetched
      queryClient.clear();
      setSession(next);
    },
    [queryClient],
  );

  const signIn = useCallback(
    async (email: string, password: string) => {
      const next = await api.login(email, password);
      adoptSession(next);
      return next;
    },
    [adoptSession],
  );

  const signInWithMicrosoft = useCallback(async () => {
    const next = await api.loginWithMicrosoft();
    adoptSession(next);
    return next;
  }, [adoptSession]);

  const refresh = useCallback(async () => {
    setSession(await api.getSession());
  }, []);

  const signOut = useCallback(async () => {
    await api.logout();
    queryClient.clear();
    setSession(null);
  }, [queryClient]);

  const value = useMemo<SessionContextValue>(
    () => ({
      session,
      initializing,
      can: (permission) => session?.permissions.includes(permission) ?? false,
      signIn,
      signInWithMicrosoft,
      adoptSession,
      refresh,
      signOut,
    }),
    [session, initializing, signIn, signInWithMicrosoft, adoptSession, refresh, signOut],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionContextValue {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error("useSession must be used inside <SessionProvider>");
  return ctx;
}
