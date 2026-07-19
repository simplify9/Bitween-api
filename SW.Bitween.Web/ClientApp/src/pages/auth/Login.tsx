import { useEffect, useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { api, type Session, type User } from "../../api";
import { DEMO_PASSWORD } from "../../api/mock/seed";
import { useSession } from "../../auth/SessionContext";
import { homePath } from "../../nav";
import { Button, FormError } from "../../components/ui/basics";
import { Field, PasswordInput, TextInput } from "../../components/ui/forms";
import { Avatar } from "../../components/ui/Avatar";
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
  const [personas, setPersonas] = useState<User[]>([]);

  useEffect(() => {
    api.demo.listPersonas().then((users) => setPersonas(users.filter((u) => u.status === "active")));
  }, []);

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
        <div className="text-center">
          <Link to="/forgot-password" className="text-[13px] font-medium text-crimson-700 hover:underline">
            Forgot your password?
          </Link>
        </div>
      </form>

      <div className="my-6 flex items-center gap-3 text-xs text-ink-400">
        <span className="h-px flex-1 bg-ink-200" />
        or
        <span className="h-px flex-1 bg-ink-200" />
      </div>

      <Button className="w-full" disabled={busy} onClick={() => void attempt(signInWithMicrosoft)}>
        <MicrosoftMark />
        Continue with Microsoft
      </Button>

      {personas.length > 0 && (
        <div className="mt-8 rounded-xl border border-dashed border-ink-300 p-4">
          <p className="text-xs font-semibold tracking-wide text-ink-500 uppercase">Prototype accounts</p>
          <p className="mt-1 mb-3 text-[13px] text-ink-500">
            One click signs you in. Every demo password is{" "}
            <code className="font-mono text-xs text-ink-700">{DEMO_PASSWORD}</code>.
          </p>
          <div className="space-y-1">
            {personas.map((p) => (
              <button
                key={p.id}
                disabled={busy}
                onClick={() => void attempt(() => signIn(p.email, DEMO_PASSWORD))}
                className="flex w-full items-center gap-2.5 rounded-lg px-2 py-1.5 text-left hover:bg-ink-50"
              >
                <Avatar name={p.displayName} size="sm" />
                <span className="min-w-0">
                  <span className="block truncate text-sm font-medium text-ink-800">{p.displayName}</span>
                  <span className="block truncate font-mono text-[11px] text-ink-500">{p.email}</span>
                </span>
              </button>
            ))}
          </div>
        </div>
      )}
    </AuthLayout>
  );
}
