import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Pencil, Plus, Trash2 } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";

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
            <MiniTable
              rows={g.attachments}
              rowKey={(a) => a.partnerId}
              empty="No partners attached — the gateway answers 401 to everyone. Attach a partner to bring it to life."
              columns={[
                {
                  header: "Partner",
                  truncate: true,
                  cell: (a) => (
                    <Link
                      to={`/partners/${a.partnerId}`}
                      className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                    >
                      {a.partnerName}
                    </Link>
                  ),
                },
                {
                  header: "Runs",
                  truncate: true,
                  cell: (a) => (
                    <Link
                      to={`/subscriptions/${a.integrationId}`}
                      className="block truncate text-[13px] text-ink-700 hover:text-crimson-700 hover:underline"
                    >
                      {a.integrationName}
                    </Link>
                  ),
                },
                {
                  header: "",
                  align: "right",
                  cell: (a) => (
                    <Can permission="api-gateways.edit">
                      <span className="flex justify-end gap-1">
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
                          className="rounded-md p-1.5 text-ink-400 hover:bg-danger-50 hover:text-danger-700"
                        >
                          <Trash2 className="size-3.5" />
                        </button>
                      </span>
                    </Can>
                  ),
                },
              ]}
            />
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
