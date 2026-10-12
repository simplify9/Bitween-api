import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useSession } from "../../auth/useSession";
import { homePath } from "../../nav";
import { FormError, LoadingBlock } from "../../components/ui/basics";
import { AuthLayout } from "./AuthLayout";
import { OIDC_RETURN_KEY } from "./Login";

/** Only a path inside this app, so a stored value can't send the browser anywhere else. */
function returnPath(): string | null {
  try {
    const stored = sessionStorage.getItem(OIDC_RETURN_KEY);
    sessionStorage.removeItem(OIDC_RETURN_KEY);
    return stored && stored.startsWith("/") && !stored.startsWith("//") ? stored : null;
  } catch {
    return null;
  }
}

/**
 * Where an OpenID Connect provider sends the browser back. The code it brings is handed to the
 * server, which exchanges it and signs in; a code is good once, so this runs once.
 */
export function OidcCallbackPage() {
  const { signInWithOidc } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [error, setError] = useState("");
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const code = params.get("code");
    const state = params.get("state");
    const refused = params.get("error_description") || params.get("error");
    if (refused || !code || !state) {
      setError(refused ? `The provider didn't sign you in: ${refused}` : "The provider sent no sign-in code.");
      return;
    }

    signInWithOidc(code, state)
      .then((session) => navigate(returnPath() ?? homePath(session), { replace: true }))
      .catch((e: unknown) => setError(e instanceof Error ? e.message : "Sign-in failed."));
  }, [params, signInWithOidc, navigate]);

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold tracking-tight text-ink-900">Signing in</h1>
      {error ? (
        <div className="mt-4 space-y-4">
          <FormError>{error}</FormError>
          <Link to="/login" className="text-sm font-medium text-crimson-700 hover:underline">
            Back to sign in
          </Link>
        </div>
      ) : (
        <LoadingBlock />
      )}
    </AuthLayout>
  );
}
