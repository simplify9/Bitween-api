import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Pencil, Plus, Trash2, Workflow } from "lucide-react";
import { api, type BusGatewayRoute } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CodeBadge, EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { matchSummary } from "../../lib/match";
import { formatDate } from "../../lib/dates";

export function BusGatewayPage() {
  const { id = "" } = useParams();
  const gatewayId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("bus-gateways.edit");

  const gateway = useQuery({
    queryKey: ["bus-gateway", gatewayId],
    queryFn: () => api.getBusGateway(gatewayId),
    retry: false,
  });

  const [name, setName] = useState("");
  const [removingRoute, setRemovingRoute] = useState<BusGatewayRoute | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && gateway.data) {
      setName(gateway.data.name);
      setLoaded(true);
    }
  }, [gateway.data, loaded]);

  const dirty = useMemo(() => !!gateway.data && name !== gateway.data.name, [gateway.data, name]);

  const save = useMutation({
    mutationFn: () => api.updateBusGateway(gatewayId, { name }),
    onSuccess: async () => {
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["bus-gateway", gatewayId] });
      void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
      setLoaded(false);
    },
  });

  if (gateway.isPending) return <LoadingBlock label="Loading bus gateway…" />;
  if (gateway.isError)
    return (
      <EmptyState title="This bus gateway no longer exists">
        <Link to="/bus-gateways" className="font-medium text-crimson-700 hover:underline">
          Back to bus gateways
        </Link>
      </EmptyState>
    );

  const g = gateway.data;

  return (
    <div className="pb-24">
      <Link
        to="/bus-gateways"
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> Bus gateways
      </Link>

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            <EditableTitle value={name} onChange={setName} disabled={!canEdit} placeholder="Gateway name" />
          </h1>
          <p className="mt-1 text-sm text-ink-500">
            Listens for{" "}
            <Link to={`/information-types/${g.informationTypeId}`} className="hover:underline">
              <CodeBadge code={g.informationTypeCode} name={g.informationTypeName} className="align-middle" />
            </Link>{" "}
            on the message bus · created {formatDate(g.createdOn)}.
          </p>
        </div>
        <Can permission="bus-gateways.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="max-w-4xl">
        <Panel
          title="Routes"
          description="Checked against every message — each matching route runs its integration."
          action={
            <Can permission="bus-gateways.edit">
              <Button size="sm" variant="primary" onClick={() => navigate(`/bus-gateways/${gatewayId}/add-route`)}>
                <Plus className="size-3.5" /> Add route
              </Button>
            </Can>
          }
        >
          {g.routes.length === 0 ? (
            <p className="text-sm text-ink-500">
              No routes — every {g.informationTypeCode} message on the bus is ignored by this
              gateway.
            </p>
          ) : (
            <ul className="space-y-2">
              {g.routes.map((r) => (
                <li key={r.id} className="rounded-xl border border-ink-200 p-3.5">
                  <div className="flex flex-wrap items-center gap-2">
                    <code className="min-w-0 flex-1 font-mono text-xs text-ink-600">
                      {matchSummary(r.matchExpression)}
                    </code>
                    <Can permission="bus-gateways.edit">
                      <span className="flex gap-1">
                        <button
                          onClick={() => navigate(`/bus-gateways/${gatewayId}/routes/${r.id}`)}
                          aria-label={`Edit route ${r.id}`}
                          className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          <Pencil className="size-3.5" />
                        </button>
                        <button
                          onClick={() => setRemovingRoute(r)}
                          aria-label={`Remove route ${r.id}`}
                          className="rounded-md p-1.5 text-ink-400 hover:bg-crimson-50 hover:text-crimson-700"
                        >
                          <Trash2 className="size-3.5" />
                        </button>
                      </span>
                    </Can>
                  </div>
                  <p className="mt-1.5 flex items-center gap-2 text-[13px] text-ink-600">
                    <Workflow className="size-3.5 shrink-0 text-ink-300" aria-hidden />
                    runs{" "}
                    <Link
                      to={`/subscriptions/${r.integrationId}`}
                      className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                    >
                      {r.integrationName}
                    </Link>
                    {r.partnerName && (
                      <>
                        for{" "}
                        <Link
                          to={`/partners/${r.partnerId}`}
                          className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                        >
                          {r.partnerName}
                        </Link>
                      </>
                    )}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Panel>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() => save.mutate()}
          onDiscard={() => setLoaded(false)}
        />
      )}

      {removingRoute && (
        <ConfirmDialog
          title="Remove this route?"
          body={
            <>
              Messages matching{" "}
              <code className="font-mono text-xs">{matchSummary(removingRoute.matchExpression)}</code>{" "}
              will stop reaching{" "}
              <strong className="font-medium text-ink-800">{removingRoute.integrationName}</strong>.
              The integration itself is kept.
            </>
          }
          confirmLabel="Remove route"
          onConfirm={async () => {
            await api.removeBusRoute(gatewayId, removingRoute.id);
            void queryClient.invalidateQueries({ queryKey: ["bus-gateway", gatewayId] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
          }}
          onClose={() => setRemovingRoute(null)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this bus gateway?"
          body={
            <>
              <strong className="font-medium text-ink-800">{g.name}</strong> stops listening and
              all its routes are removed. The integrations behind them are kept.
            </>
          }
          confirmLabel="Delete gateway"
          onConfirm={async () => {
            await api.deleteBusGateway(gatewayId);
            void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
            void queryClient.invalidateQueries({ queryKey: ["integrations"] });
            navigate("/bus-gateways");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
