import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Handshake, Pencil, Plus, Trash2 } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { formatDate } from "../../lib/dates";

export function ApiGatewayPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("api-gateways.edit");

  const gateway = useQuery({
    queryKey: ["api-gateway", gatewayId],
    queryFn: () => api.getApiGateway(gatewayId),
    retry: false,
  });

  const [name, setName] = useState("");
  const [urlName, setUrlName] = useState("");
  const [removing, setRemoving] = useState<{ partnerId: number; partnerName: string } | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && gateway.data) {
      setName(gateway.data.name);
      setUrlName(gateway.data.urlName);
      setLoaded(true);
    }
  }, [gateway.data, loaded]);

  const dirty = useMemo(
    () => !!gateway.data && (name !== gateway.data.name || urlName !== gateway.data.urlName),
    [gateway.data, name, urlName],
  );

  const save = useMutation({
    mutationFn: () => api.updateApiGateway(gatewayId, { name, urlName }),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["api-gateway", gatewayId] });
      void queryClient.invalidateQueries({ queryKey: ["api-gateways"] });
      setLoaded(false);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading API gateway…" />;
  if (gateway.isError)
    return (
      <EmptyState title="This API gateway no longer exists">
        <Link to="/api-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to API gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;

  return (
    <div className="pb-24">
      <Link
        to="/api-gateways"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> API gateways
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle value={name} onChange={setName} disabled={!canEdit} placeholder="Gateway name" />
          </h1>
          <p className="mt-1 text-sm text-ink-500">Created {formatDate(g.createdOn)}.</p>
        </div>
        <Can permission="api-gateways.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          <Panel
            title="Partners"
            description="Each attached partner calls this gateway with its API key and runs its own integration."
            action={
              <Can permission="api-gateways.edit">
                <Button size="sm" variant="primary" onClick={() => navigate(`/api-gateways/${gatewayId}/attach`)}>
                  <Plus className="size-3.5" /> Attach partner
                </Button>
              </Can>
            }
          >
            {g.attachments.length === 0 ? (
              <p className="text-sm text-ink-500">
                No partners attached — the gateway answers 401 to everyone. Attach a partner to
                bring it to life.
              </p>
            ) : (
              <ul className="divide-y divide-ink-100">
                {g.attachments.map((a) => (
                  <li key={a.partnerId} className="flex items-center gap-3 py-2.5 first:pt-0 last:pb-0">
                    <Handshake className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                    <span className="min-w-0 flex-1">
                      <Link
                        to={`/partners/${a.partnerId}`}
                        className="block truncate text-sm font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                      >
                        {a.partnerName}
                      </Link>
                      <span className="block truncate text-[13px] text-ink-500">
                        runs{" "}
                        <Link
                          to={`/subscriptions/${a.integrationId}`}
                          className="font-medium text-ink-700 hover:text-crimson-700 hover:underline"
                        >
                          {a.integrationName}
                        </Link>
                      </span>
                    </span>
                    <Can permission="api-gateways.edit">
                      <span className="flex gap-1">
                        <button
                          onClick={() => navigate(`/api-gateways/${gatewayId}/attachments/${a.partnerId}`)}
                          aria-label={`Edit attachment for ${a.partnerName}`}
                          className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          <Pencil className="size-3.5" />
                        </button>
                        <button
                          onClick={() => setRemoving({ partnerId: a.partnerId, partnerName: a.partnerName })}
                          aria-label={`Detach ${a.partnerName}`}
                          className="rounded-md p-1.5 text-ink-400 hover:bg-crimson-50 hover:text-crimson-700"
                        >
                          <Trash2 className="size-3.5" />
                        </button>
                      </span>
                    </Can>
                  </li>
                ))}
              </ul>
            )}
          </Panel>
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Endpoint" description="Partners authenticate with their API key.">
            <div className="space-y-4">
              <Field label="URL name" htmlFor="ag-url">
                <TextInput
                  id="ag-url"
                  value={urlName}
                  disabled={!canEdit}
                  className="font-mono"
                  onChange={(e) => setUrlName(e.target.value.toLowerCase())}
                />
              </Field>
              <CopyField value={`/api/Gateway/${urlName}/sync`} label="Synchronous — waits for the result" />
              <CopyField value={`/api/Gateway/${urlName}/async`} label="Asynchronous — returns the exchange id" />
            </div>
          </Panel>
        </div>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {removing && (
        <ConfirmDialog
          title="Detach this partner?"
          body={
            <>
              <strong className="font-medium text-ink-800">{removing.partnerName}</strong> will get
              401s from this gateway immediately. The integration itself is kept.
            </>
          }
          confirmLabel="Detach partner"
          onConfirm={async () => {
            await api.removeGatewayAttachment(gatewayId, removing.partnerId);
            void queryClient.invalidateQueries({ queryKey: ["api-gateway", gatewayId] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
          }}
          onClose={() => setRemoving(null)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this API gateway?"
          body={
            <>
              <strong className="font-medium text-ink-800">{g.name}</strong> and its partner
              attachments will be gone; partners calling it start getting 404s. The integrations
              behind it are kept.
            </>
          }
          confirmLabel="Delete gateway"
          onConfirm={async () => {
            await api.deleteApiGateway(gatewayId);
            void queryClient.invalidateQueries({ queryKey: ["api-gateways"] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
            navigate("/api-gateways");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
