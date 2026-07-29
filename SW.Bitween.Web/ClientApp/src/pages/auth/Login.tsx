import { useEffect, useState, type FormEvent } from "react";
import { useLocation, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { getAppConfig, type Session } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { homePath } from "../../nav";
import { Button, FormError } from "../../components/ui/basics";
import { Field, PasswordInput, TextInput } from "../../components/ui/forms";
import { AuthLayout } from "./AuthLayout";

function MicrosoftMark() {
  return (
    <svg viewBox="0 0 21 21" className="size-4" aria-hidden>
      <rect x="1" y="1" width="9" height="9" fill="#f25022" />
      <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
      <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
      <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
    </svg>
  );
}

export function LoginPage() {
  const { session, signIn, signInWithMicrosoft } = useSession();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from;

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  // Microsoft sign-in only shows when the backend has MSAL configured.
  const appConfig = useQuery({ queryKey: ["app-config"], queryFn: getAppConfig });
  const microsoftEnabled = Boolean(appConfig.data?.msalClientId);
  // The Login handler rejects email/password outright when this instance is Microsoft-only, so
  // offering the form would only ever produce a failed sign-in.
  const passwordEnabled = !appConfig.data?.disableEmailPasswordLogin;

  // already signed in (e.g. back-button to /login)
  useEffect(() => {
    if (session) navigate(from ?? homePath(session), { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const finish = (next: Session) => navigate(from ?? homePath(next), { replace: true });

  const attempt = async (fn: () => Promise<Session>) => {
    setBusy(true);
    setError("");
    try {
      finish(await fn());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Sign-in failed.");
    } finally {
      setBusy(false);
    }
  };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    void attempt(() => signIn(email, password));
  };

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold tracking-tight text-ink-900">Sign in</h1>
      <p className="mt-1 mb-6 text-sm text-ink-500">Welcome back to Bitween.</p>

      {passwordEnabled && (
        <form onSubmit={submit} className="space-y-4">
          <Field label="Email" htmlFor="login-email">
            <TextInput
              id="login-email"
              type="email"
              autoComplete="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="you@company.com"
            />
          </Field>
          <Field label="Password" htmlFor="login-password">
            <PasswordInput
              id="login-password"
              autoComplete="current-password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </Field>
          <FormError>{error}</FormError>
          <Button type="submit" variant="primary" busy={busy} className="w-full">
            Sign in
          </Button>
        </form>
      )}

      {microsoftEnabled && (
        <>
          {passwordEnabled ? (
            <div className="my-6 flex items-center gap-3 text-xs text-ink-400">
              <span className="h-px flex-1 bg-ink-200" />
              or
              <span className="h-px flex-1 bg-ink-200" />
            </div>
          ) : (
            <div className="mb-4">
              <FormError>{error}</FormError>
            </div>
          )}

          <Button
            className="w-full"
            variant={passwordEnabled ? undefined : "primary"}
            disabled={busy}
            onClick={() => void attempt(signInWithMicrosoft)}
          >
            <MicrosoftMark />
            Continue with Microsoft
          </Button>
        </>
      )}

      {/* Microsoft-only with no MSAL configured locks everyone out — say so rather than
          rendering an empty card. */}
      {!passwordEnabled && !microsoftEnabled && (
        <FormError>
          This instance is set to Microsoft sign-in only, but Microsoft sign-in isn&apos;t configured. An
          administrator needs to add the Azure AD client ID, tenant ID and redirect URI.
        </FormError>
      )}
    </AuthLayout>
  );
}
