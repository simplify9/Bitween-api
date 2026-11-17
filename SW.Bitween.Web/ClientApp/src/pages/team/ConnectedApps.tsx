import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { disconnectApp, listConnectedApps, type ConnectedApp } from "../../api";
import { Button, LoadError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { timeAgo } from "../../lib/dates";

/**
 * The AI apps connected as an account - signed in with OAuth to use Bitween at /mcp - and a way to
 * disconnect each. On the profile for the member's own, in the member drawer for an administrator.
 */
export function ConnectedAppsList({ accountId, canDisconnect }: { accountId?: string; canDisconnect: boolean }) {
  const queryClient = useQueryClient();
  const key = ["connected-apps", accountId ?? "me"];
  const apps = useQuery({ queryKey: key, queryFn: () => listConnectedApps(accountId) });
  const [confirming, setConfirming] = useState<ConnectedApp | null>(null);
  const disconnect = useMutation({
    mutationFn: (app: ConnectedApp) => disconnectApp(app.clientId, accountId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
  });

  if (apps.isLoading) return <LoadingBlock label="Loading connected apps…" />;
  if (apps.error) return <LoadError error={apps.error} what="connected apps" onRetry={() => void apps.refetch()} />;
  if (!apps.data?.length) return <p className="text-sm text-ink-500">No AI apps connected.</p>;

  return (
    <>
      <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
        {apps.data.map((app) => (
          <li key={app.clientId} className="flex items-start gap-3 px-3 py-2.5">
            <div className="min-w-0 flex-1">
              <p className="text-sm font-medium text-ink-900">{app.name}</p>
              <p className="text-xs text-ink-500" title="Its sign-in renews about once a session's length while it's in use.">
                Last renewed {timeAgo(app.lastRenewedOn)}
              </p>
            </div>
            {canDisconnect && (
              <Button
                size="sm"
                variant="danger"
                title="Ends its sign-in. It has to be connected again to use Bitween."
                onClick={() => setConfirming(app)}
              >
                Disconnect
              </Button>
            )}
          </li>
        ))}
      </ul>
      {confirming && (
        <ConfirmDialog
          title={`Disconnect ${confirming.name}?`}
          body="It can't renew its sign-in after this. Access it already holds ends within one sign-in session's length."
          confirmLabel="Disconnect"
          onConfirm={async () => {
            await disconnect.mutateAsync(confirming);
          }}
          onClose={() => setConfirming(null)}
        />
      )}
    </>
  );
}
