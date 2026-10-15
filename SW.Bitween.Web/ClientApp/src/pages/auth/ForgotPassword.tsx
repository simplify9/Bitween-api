import { useState, type FormEvent } from "react";
import { Link, useLocation } from "react-router";
import { useMutation } from "@tanstack/react-query";
import { MailCheck } from "lucide-react";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { AuthLayout } from "./AuthLayout";

/**
 * "Forgot password?": asks for an address and emails it a reset link.
 *
 * The answer is the same whether the address has an account or not - the server sends the same
 * reply either way - so this page can't be used to find out who has one.
 */
export function ForgotPasswordPage() {
  const location = useLocation();
  const [email, setEmail] = useState((location.state as { email?: string } | null)?.email ?? "");

  const send = useMutation({ mutationFn: () => api.forgotPassword(email.trim()) });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    send.mutate();
  };

  return (
    <AuthLayout>
      {send.isSuccess ? (
        <div role="status">
          <MailCheck className="mb-3 size-8 text-ok-600" aria-hidden />
          <h1 className="text-xl font-semibold tracking-tight text-ink-900">Check your email</h1>
          <p className="mt-2 text-sm text-ink-600">
            If <span className="font-medium text-ink-900">{email.trim()}</span> belongs to a member, a link
            to choose a new password is on its way. It works once, for 1 hour.
          </p>
          <p className="mt-2 text-sm text-ink-500">
            Nothing after a few minutes? Check your spam folder, or ask your administrator.
          </p>
          <Link to="/login" className="mt-6 inline-block text-sm font-medium link">
            Back to sign in
          </Link>
        </div>
      ) : (
        <>
          <h1 className="text-xl font-semibold tracking-tight text-ink-900">Reset your password</h1>
          <p className="mt-1 mb-6 text-sm text-ink-500">
            Enter the email you sign in with, and we&apos;ll send you a link to choose a new password.
          </p>
          <form onSubmit={submit} className="space-y-4">
            <Field label="Email" htmlFor="forgot-email">
              <TextInput
                id="forgot-email"
                type="email"
                autoComplete="email"
                required
                autoFocus
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="you@company.com"
              />
            </Field>
            <FormError>{send.error?.message}</FormError>
            <Button type="submit" variant="primary" busy={send.isPending} className="w-full">
              Send reset link
            </Button>
          </form>
          <Link to="/login" className="mt-6 inline-block text-sm font-medium link">
            Back to sign in
          </Link>
        </>
      )}
    </AuthLayout>
  );
}
