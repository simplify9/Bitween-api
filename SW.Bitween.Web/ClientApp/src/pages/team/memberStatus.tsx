import { type User } from "../../api";
import { Badge } from "../../components/ui/basics";
import { formatDate } from "../../lib/dates";

/** Whether an invite still works. */
export const inviteActive = (user: Pick<User, "invite">) =>
  Boolean(user.invite && new Date(user.invite.expiresOn) > new Date());

/**
 * @param passwordsOff The instance signs in with Microsoft or single sign-on only, where no
 *   member has a password and that is nothing to report.
 */
export function statusBadge(user: User, passwordsOff = false) {
  if (user.status !== "active") return <Badge tone="neutral">Disabled</Badge>;
  // A lockout expires on its own, so it is reported with its own tone rather than
  // as a failure - and never as "Active", which would say the opposite of the truth.
  if (user.lockedUntil) return <Badge tone="warn">Locked</Badge>;
  // Invited and yet to choose a password: they can't sign in, which "Active" would hide.
  if (!user.hasPassword && !passwordsOff) {
    if (inviteActive(user))
      return (
        <Badge tone="neutral" title={`Invited. They can choose a password until ${formatDate(user.invite!.expiresOn)}.`}>
          Invited
        </Badge>
      );
    return (
      <Badge tone="warn" title="No password yet, and no invite that still works. Send a new invite.">
        {user.invite ? "Invite expired" : "Not invited"}
      </Badge>
    );
  }
  return <Badge tone="ok">Active</Badge>;
}
