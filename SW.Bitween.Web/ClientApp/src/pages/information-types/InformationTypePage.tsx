import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, Trash2, Undo2 } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Badge, Button, EmptyState, isNotFound, LoadError, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CodeBadge, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { HistoryCard } from "../../components/config/HistoryCard";
import { ExchangesList, SetupList } from "../../components/config/shared";
import { BackLink } from "../../components/ui/BackLink";
import { keys } from "../../api/queryKeys";
import { formatDate } from "../../lib/dates";
import { useLeaveGuard } from "../../lib/useLeaveGuard";
import {
  BusTypeRenameConfirm,
  InformationTypeFields,
  InformationTypeMessagesWarning,
  renamesBusQueue,
  informationTypeChanges,
  informationTypeDirty,
  informationTypeDraftOf,
  type InformationTypeDraft,
} from "../../components/config/InformationTypeFields";

export function InformationTypePage() {
  const { id = "" } = useParams();
  const typeId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("documents.edit");

  const type = useQuery({
    queryKey: keys.informationTypes.detail(typeId),
    queryFn: () => api.getInformationType(typeId),
    retry: false,
  });

  const [draft, setDraft] = useState<InformationTypeDraft | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirmingRename, setConfirmingRename] = useState(false);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (!loaded && type.data) {
      setDraft(informationTypeDraftOf(type.data));
      setLoaded(true);
    }
  }, [type.data, loaded]);

  const dirty = useMemo(
    () => !!type.data && !!draft && informationTypeDirty(draft, informationTypeDraftOf(type.data)),
    [type.data, draft],
  );

  const save = useMutation({
    mutationFn: () => api.updateInformationType(typeId, informationTypeChanges(draft!)),
    onSuccess: async () => {
      // Awaited before the draft is re-synced, or the re-sync would seed from stale data.
      await queryClient.invalidateQueries({ queryKey: keys.informationTypes.all });
      setLoaded(false);
    },
  });

  const retire = useMutation({
    mutationFn: () => api.retireInformationType(typeId),
    // Every picker reads the list, and the sidebar count with it, so the whole family
    // is refreshed rather than just this type's own entry.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.informationTypes.all }),
  });

  const { leave, dialog: leaveDialog } = useLeaveGuard(dirty);

  if (type.isPending) return <LoadingBlock label="Loading information type…" />;
  if (type.isError && !isNotFound(type.error))
    return <LoadError error={type.error} what="this information type" onRetry={() => void type.refetch()} />;
  if (type.isError)
    return (
      <EmptyState title="This information type no longer exists">
        <Link to="/information-types" className="font-medium text-crimson-700 hover:underline">
          Back to information types
        </Link>
      </EmptyState>
    );

  const t = type.data;

  return (
    <div className="pb-24">
      {leaveDialog}
      <BackLink to="/information-types" label="Information types" />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            {t.name}
            <CodeBadge code={t.code} name={t.name} />
            {t.isSystem && (
              <Badge
                tone="ink"
                title="Seeded by Bitween and used by aggregation itself — it can't be retired or deleted."
              >
                Built-in
              </Badge>
            )}
            {t.retiredOn && (
              <Badge
                tone="neutral"
                title={`Taken out of use on ${formatDate(t.retiredOn)}. Its exchanges still name it; it is no longer offered for new work.`}
              >
                Retired
              </Badge>
            )}
          </h1>
        </div>
        {/*
          The built-in type is the backend's, not the user's: neither action is offered for it
          rather than offered and then refused.
        */}
        {!t.isSystem && (
          <div className="flex flex-col items-end gap-1.5">
            <div className="flex flex-wrap items-center gap-2">
              {/*
                Ahead of Delete, and the one offered by default: a type that has carried traffic
                almost always wants to leave the pickers rather than take its exchanges with it,
                and this is the half of the pair that can be undone.
              */}
              <Can permission="documents.edit">
                <Button onClick={() => retire.mutate()} busy={retire.isPending}>
                  {t.retiredOn ? (
                    <>
                      <Undo2 className="size-4" /> Restore
                    </>
                  ) : (
                    <>
                      <Archive className="size-4" /> Retire
                    </>
                  )}
                </Button>
              </Can>
              <Can permission="documents.delete">
                <Button variant="danger" onClick={() => setDeleting(true)}>
                  <Trash2 className="size-4" /> Delete
                </Button>
              </Can>
            </div>
            {/*
              Retire refuses for reasons the user can act on — a subscription still carrying the
              type, say — and without this the refusal reached the network tab and nowhere else.
            */}
            {retire.error && (
              <p className="max-w-md text-right text-[13px] text-danger-700">
                {retire.error.message}
              </p>
            )}
          </div>
        )}
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0">
          {draft && (
            <InformationTypeFields draft={draft} onChange={setDraft} canEdit={canEdit} saved={t} />
          )}
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Used by" description="Everything that carries or routes this type.">
            <div className="space-y-4">
              <SetupList items={t.subscriptionSetups} />
              {t.busGateways.length > 0 && (
                <div className="border-t border-ink-100 pt-3">
                  <MiniTable
                    rows={t.busGateways}
                    rowKey={(g) => g.gatewayId}
                    search={{ text: (g) => g.gatewayName, noun: "bus gateways" }}
                    empty=""
                    columns={[
                      {
                        header: "Bus gateway",
                        wrap: true,
                        cell: (g) => (
                          <Link
                            to={`/bus-gateways/${g.gatewayId}`}
                            className="block font-medium text-ink-800 hover:text-crimson-700 hover:underline"
                          >
                            {g.gatewayName}
                          </Link>
                        ),
                      },
                    ]}
                  />
                </div>
              )}
            </div>
          </Panel>

          <Can permission="exchanges.view">
            <Panel title="Recent exchanges" description="Latest traffic carrying this type.">
              <ExchangesList items={t.recentExchanges} hide={["type"]} />
            </Panel>
          </Can>

          <HistoryCard entityName="Document" entityKey={id} />
        </div>
      </div>

      {canEdit && dirty && (
        <UnsavedBar
          busy={save.isPending}
          error={save.error?.message}
          onSave={() =>
            draft && renamesBusQueue(t.busMessageTypeName, draft) ? setConfirmingRename(true) : save.mutate()
          }
          onDiscard={() => setLoaded(false)}
        />
      )}

      {deleting && (
        <ConfirmDialog
          title="Delete this information type?"
          body={
            <div className="space-y-2">
              <p>
                <strong className="font-medium text-ink-800">{t.code ?? t.name}</strong> and its promoted
                properties will be gone for good — <strong className="font-medium text-ink-800">along with
                every exchange recorded against it</strong>, and their payloads. A type still carried by a
                subscription, or listened for by a bus gateway, can't be deleted.
              </p>
              {t.busMessageTypeName && (
                <>
                  <p>Its bus queue is deleted too.</p>
                  <InformationTypeMessagesWarning typeId={typeId} />
                </>
              )}
            </div>
          }
          confirmLabel="Delete information type"
          onConfirm={async () => {
            await api.deleteInformationType(typeId);
            void queryClient.invalidateQueries({ queryKey: keys.informationTypes.all });
            leave(() => navigate("/information-types", { replace: true }));
          }}
          onClose={() => setDeleting(false)}
        />
      )}

      {confirmingRename && draft && t.busMessageTypeName && (
        <BusTypeRenameConfirm
          typeId={typeId}
          from={t.busMessageTypeName}
          to={draft.busMessageTypeName.trim()}
          onConfirm={async () => {
            await save.mutateAsync();
          }}
          onClose={() => setConfirmingRename(false)}
        />
      )}
    </div>
  );
}
