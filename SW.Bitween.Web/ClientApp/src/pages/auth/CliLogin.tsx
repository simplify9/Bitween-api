import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { Terminal } from "lucide-react";
import { grantCliSignIn } from "../../api";
import { useSession } from "../../auth/useSession";
import { Button, FormError } from "../../components/ui/basics";
import { AuthLayout } from "./AuthLayout";

const CHALLENGE = /^[A-Za-z0-9_-]{43}$/;
const STATE = /^[A-Za-z0-9_-]{8,128}$/;

/** Only a port on this machine's loopback: the code must never be sent anywhere else. */
function loopbackPort(raw: string | null): number | null {
  if (!raw || !/^\d{1,5}$/.test(raw)) return null;
  const port = Number(raw);
  return port >= 1024 && port <= 65535 ? port : null;
}

/**
 * Where `bitween login` sends the browser. The member is signed in here already, however they
 * signed in, Microsoft included; confirming hands the CLI a one-time code for its own session. The
 * code goes back to the CLI's listener on 127.0.0.1, or is shown to paste when the CLI runs on
 * another machine (`--no-browser`). Asking first matters: a link to this page from anyone else
 * must not sign a terminal in without the member saying so.
 */
export function CliLoginPage() {
  const { session } = useSession();
  const [params] = useSearchParams();
  const challenge = params.get("challenge") ?? "";
  const state = params.get("state") ?? "";
  const port = loopbackPort(params.get("port"));
  const valid = CHALLENGE.test(challenge) && STATE.test(state) && (params.get("port") === null || port !== null);

  const [shown, setShown] = useState<string | null>(null);
  const [done, setDone] = useState<"signed-in" | "cancelled" | null>(null);

  const callback = (query: string) => `http://127.0.0.1:${port}/callback?${query}&state=${encodeURIComponent(state)}`;

  const grant = useMutation({
    mutationFn: () => grantCliSignIn(challenge),
    onSuccess: ({ code }) => {
      if (port !== null) {
        setDone("signed-in");
        window.location.assign(callback(`code=${encodeURIComponent(code)}`));
      } else setShown(code);
    },
  });

  const cancel = () => {
    setDone("cancelled");
    if (port !== null) window.location.assign(callback("error=cancelled"));
  };

  return (
    <AuthLayout>
      <div className="flex items-center gap-3">
        <span className="flex size-10 items-center justify-center rounded-full bg-ink-100 text-ink-600">
          <Terminal className="size-5" />
        </span>
        <h1 className="text-2xl font-semibold tracking-tight text-ink-900">Sign in the bitween CLI</h1>
      </div>

      {!valid ? (
        <p className="mt-4 text-sm text-ink-600">
          This link isn't a sign-in request from the bitween CLI. Run <code>bitween login</code> again to get a new one.
        </p>
      ) : shown ? (
        <>
          <p className="mt-4 text-sm text-ink-600">Paste this code into the terminal. It works once, for two minutes.</p>
          <pre className="mt-3 overflow-x-auto rounded-md bg-ink-50 px-3 py-2 font-mono text-xs break-all whitespace-pre-wrap text-ink-900">
            {shown}
          </pre>
          <Button className="mt-3" onClick={() => navigator.clipboard?.writeText(shown)}>
            Copy
          </Button>
        </>
      ) : done === "cancelled" ? (
        <p className="mt-4 text-sm text-ink-600">Cancelled. The CLI wasn't signed in. You can close this tab.</p>
      ) : done === "signed-in" ? (
        <p className="mt-4 text-sm text-ink-600">Handing the sign-in back to the CLI…</p>
      ) : (
        <>
          <p className="mt-4 text-sm text-ink-600">
            A bitween CLI {port !== null ? "on this computer " : ""}is asking to sign in to this Bitween as you. It
            will be able to do what you can do here, until you run <code>bitween logout</code> or it goes unused for
            30 days.
          </p>
          {session && (
            <p className="mt-3 rounded-md bg-ink-50 px-3 py-2 text-sm text-ink-600">
              Signed in as <span className="font-medium text-ink-900">{session.user.email}</span>
            </p>
          )}
          <p className="mt-3 text-sm text-ink-500">Only continue if you just ran <code>bitween login</code>.</p>
          {grant.error && (
            <div className="mt-3">
              <FormError>{grant.error.message}</FormError>
            </div>
          )}
          <div className="mt-6 flex gap-2">
            <Button onClick={cancel} disabled={grant.isPending}>
              Cancel
            </Button>
            <Button variant="primary" busy={grant.isPending} onClick={() => grant.mutate()}>
              Sign in the CLI
            </Button>
          </div>
        </>
      )}
    </AuthLayout>
  );
}
