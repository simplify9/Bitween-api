import { useState, type FormEvent } from "react";
import { Link } from "react-router";
import { ArrowLeft, MailCheck } from "lucide-react";
import { api } from "../../api";
import { Button } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { CopyField } from "../../components/ui/CopyField";
import { AuthLayout } from "./AuthLayout";

export function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [demoLink, setDemoLink] = useState("");

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    const { resetLink } = await api.requestPasswordReset(email);
    setDemoLink(resetLink);
    setSent(true);
    setBusy(false);
  };

  return (
    <AuthLayout>
      {sent ? (
        <div className="space-y-4">
          <span className="flex size-11 items-center justify-center rounded-full bg-ok-100 text-ok-600">
            <MailCheck className="size-5" />
          </span>
          <h1 className="text-xl font-semibold tracking-tight text-ink-900">Check your inbox</h1>
          <p className="text-sm leading-relaxed text-ink-500">
            If an account exists for <strong className="font-medium text-ink-700">{email}</strong>, a
            link to choose a new password is on its way.
          </p>
          {demoLink && (
            <div className="rounded-xl border border-dashed border-ink-300 p-4">
              <p className="mb-2 text-[13px] text-ink-500">
                Prototype: email isn't wired up, so here's the link the email would contain.
              </p>
              <CopyField value={demoLink} />
            </div>
          )}
          <Link to="/login" className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline">
            <ArrowLeft className="size-3.5" /> Back to sign in
          </Link>
        </div>
      ) : (
        <>
          <h1 className="text-xl font-semibold tracking-tight text-ink-900">Reset your password</h1>
          <p className="mt-1 mb-6 text-sm text-ink-500">
            Enter your email and we'll send you a link to choose a new one.
          </p>
          <form onSubmit={(e) => void submit(e)} className="space-y-4">
            <Field label="Email" htmlFor="fp-email">
              <TextInput
                id="fp-email"
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="you@company.com"
              />
            </Field>
            <Button type="submit" variant="primary" busy={busy} className="w-full">
              Send reset link
            </Button>
            <div className="text-center">
              <Link to="/login" className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline">
                <ArrowLeft className="size-3.5" /> Back to sign in
              </Link>
            </div>
          </form>
        </>
      )}
    </AuthLayout>
  );
}
