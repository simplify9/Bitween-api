import { Link } from "react-router";
import type { AdapterUse, RegistryRevokedVersion } from "../../api";
import { InlineNotice } from "../ui/basics";
import { revokedRun, useRegistryChecks } from "./registryChecks";

const SLOT: Record<AdapterUse["kind"], string> = {
  receiver: "receiver",
  validator: "validator",
  mapper: "mapper",
  handler: "handler",
};

/** One line per slot that runs a version the registry has revoked. Nothing when none does. */
export function RevokedAdapterNotice({ uses }: { uses: AdapterUse[] }) {
  const { byAdapter } = useRegistryChecks();
  const flagged = uses
    .map((use) => ({ use, revoked: revokedRun(use, byAdapter.get(use.adapterId.toLowerCase())) }))
    .filter((x): x is { use: AdapterUse; revoked: RegistryRevokedVersion } => x.revoked !== null);
  if (flagged.length === 0) return null;

  return (
    <div role="status">
      {flagged.map(({ use, revoked }) => (
        <InlineNotice key={use.kind}>
          The {SLOT[use.kind]} runs <code className="font-mono">{use.adapterId}</code> v{revoked.version}, which the
          registry has revoked{revoked.reason ? `: ${revoked.reason}` : "."} Pin another version, or make one current on
          the{" "}
          <Link to="/adapters?tab=installed" className="font-medium underline">
            Adapters page
          </Link>
          .
        </InlineNotice>
      ))}
    </div>
  );
}
