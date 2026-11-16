import type { ReactNode } from "react";
import { useSession } from "../../auth/useSession";
import { Button, FormError } from "../../components/ui/basics";

/**
 * Asking the member before a program signs in as them - the bitween CLI, or an AI app. Says who's
 * asking and as whom, warns what to check, and offers Cancel and the approval. A link to such a page
 * from anyone else must never sign a program in without the member saying so.
 */
export function SignInConsent({
  children,
  warning,
  approveLabel,
  busy,
  error,
  onApprove,
  onCancel,
}: {
  /** What the program is and what it will be able to do. */
  children: ReactNode;
  /** What to make sure of before approving. */
  warning: ReactNode;
  approveLabel: string;
  busy: boolean;
  error?: string;
  onApprove: () => void;
  onCancel: () => void;
}) {
  const { session } = useSession();
  return (
    <>
      <div className="mt-4 space-y-2 text-sm text-ink-600">{children}</div>
      {session && (
        <p className="mt-3 rounded-md bg-ink-50 px-3 py-2 text-sm text-ink-600">
          Signed in as <span className="font-medium text-ink-900">{session.user.email}</span>
        </p>
      )}
      <p className="mt-3 text-sm text-ink-500">{warning}</p>
      {error && (
        <div className="mt-3">
          <FormError>{error}</FormError>
        </div>
      )}
      <div className="mt-6 flex gap-2">
        <Button onClick={onCancel} disabled={busy}>
          Cancel
        </Button>
        <Button variant="primary" busy={busy} onClick={onApprove}>
          {approveLabel}
        </Button>
      </div>
    </>
  );
}
