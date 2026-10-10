import { type UserStatus } from "../../api";
import { Badge } from "../../components/ui/basics";

export function statusBadge(status: UserStatus, lockedUntil?: string | null) {
  if (status !== "active") return <Badge tone="neutral">Disabled</Badge>;
  // A lockout expires on its own, so it is reported with its own tone rather than
  // as a failure — and never as "Active", which would say the opposite of the truth.
  if (lockedUntil) return <Badge tone="warn">Locked</Badge>;
  return <Badge tone="ok">Active</Badge>;
}
