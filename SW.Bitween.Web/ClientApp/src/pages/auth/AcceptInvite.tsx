import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { MailX } from "lucide-react";
import { api, ApiRequestError } from "../../api";
import { useSession } from "../../auth/SessionContext";
import { homePath } from "../../nav";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, PasswordInput, TextInput } from "../../components/ui/forms";
import { AuthLayout } from "./AuthLayout";

export function AcceptInvitePage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const { adoptSession } = useSession();

  const invite = useQuery({
    queryKey: ["invite", token],
    queryFn: () => api.getInvite(token),
    retry: false,
  });

  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (password !== confirm) {
      setError("The passwords don't match.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      const session = await api.acceptInvite(token, { displayName, password });
      adoptSession(session);
      navigate(homePath(session), { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Couldn't finish setting up your account.");
    } finally {
      setBusy(false);
    }
  };

  if (invite.isPending) {
    return (
      <AuthLayout>
        <LoadingBlock label="Checking your invite…" />
      </AuthLayout>
    );
  }

  if (invite.isError) {
    return (
      <AuthLayout>
        <div className="space-y-4">
          <span className="flex size-11 items-center justify-center rounded-full bg-crimson-50 text-crimson-600">
            <MailX className="size-5" />
          </span>
          <h1 className="text-xl font-semibold tracking-tight text-ink-900">
            This invite doesn't work anymore
          </h1>
          <p className="text-sm text-ink-500">
            {invite.error instanceof ApiRequestError
              ? invite.error.message
              : "The link may have expired or been revoked. Ask your administrator to send a new one."}
          </p>
          <Link to="/login" className="text-[13px] font-medium text-crimson-700 hover:underline">
            Go to sign in
          </Link>
        </div>
      </AuthLayout>
    );
  }

  const inv = invite.data;
  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold tracking-tight text-ink-900">Join your team on Bitween</h1>
      <p className="mt-1 mb-4 text-sm text-ink-500">
        {inv.invitedByName} invited <strong className="font-medium text-ink-700">{inv.email}</strong>.
      </p>
      <div className="mb-6 flex flex-wrap items-center gap-1.5">
        <span className="text-[13px] text-ink-500">You'll join as</span>
        {inv.roleNames.map((name) => (
          <Badge key={name} tone="crimson">
            {name}
          </Badge>
        ))}
      </div>

      <form onSubmit={(e) => void submit(e)} className="space-y-4">
        <Field label="Your name" htmlFor="ai-name" hint="How teammates will see you.">
          <TextInput
            id="ai-name"
            required
            autoComplete="name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Jane Kawar"
          />
        </Field>
        <Field label="Password" htmlFor="ai-password" hint="At least 8 characters.">
          <PasswordInput
            id="ai-password"
            required
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </Field>
        <Field label="Confirm password" htmlFor="ai-confirm">
          <PasswordInput
            id="ai-confirm"
            required
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </Field>
        <FormError>{error}</FormError>
        <Button type="submit" variant="primary" busy={busy} className="w-full">
          Create my account
        </Button>
      </form>
    </AuthLayout>
  );
}
