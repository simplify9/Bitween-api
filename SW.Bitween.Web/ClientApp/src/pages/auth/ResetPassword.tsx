import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, PasswordInput } from "../../components/ui/forms";
import { AuthLayout } from "./AuthLayout";

export function ResetPasswordPage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
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
      await api.resetPassword(token, password);
      navigate("/login", { replace: true });
    } catch (err) {
      setError(err instanceof Error ? err.message : "The reset link didn't work.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold tracking-tight text-ink-900">Choose a new password</h1>
      <p className="mt-1 mb-6 text-sm text-ink-500">You'll sign in with it from now on.</p>
      <form onSubmit={(e) => void submit(e)} className="space-y-4">
        <Field label="New password" htmlFor="rp-password" hint="At least 8 characters.">
          <PasswordInput
            id="rp-password"
            required
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </Field>
        <Field label="Confirm new password" htmlFor="rp-confirm">
          <PasswordInput
            id="rp-confirm"
            required
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
          />
        </Field>
        <FormError>{error}</FormError>
        <Button type="submit" variant="primary" busy={busy} className="w-full">
          Save password
        </Button>
        <div className="text-center">
          <Link to="/login" className="text-[13px] font-medium text-crimson-700 hover:underline">
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthLayout>
  );
}
