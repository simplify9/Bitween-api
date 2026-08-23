import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { api } from "../../api";
import { Can, useSessionCan } from "../../auth/guards";
import { Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { ConfirmDialog } from "../../components/ui/overlays";
import { CodeBadge, Panel, UnsavedBar } from "../../components/ui/Panel";
import { MiniTable } from "../../components/ui/Table";
import { ExchangesList, SetupList, TrailTable } from "../../components/config/shared";
import { BackLink } from "../../components/ui/BackLink";
import {
  InformationTypeFields,
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
    queryKey: ["information-type", typeId],
    queryFn: () => api.getInformationType(typeId),
    retry: false,
  });

  const [draft, setDraft] = useState<InformationTypeDraft | null>(null);
  const [deleting, setDeleting] = useState(false);
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
      // Await the detail refetch before re-syncing the draft (avoids stale-data race).
      await queryClient.invalidateQueries({ queryKey: ["information-type", typeId] });
      void queryClient.invalidateQueries({ queryKey: ["information-types"] });
      setLoaded(false);
    },
  });

  if (type.isPending) return <LoadingBlock label="Loading information type…" />;
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
      <BackLink to="/information-types" label="Information types" />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2.5 text-[22px] font-semibold tracking-tight text-ink-900">
            {t.name}
            <CodeBadge code={t.code} name={t.name} />
          </h1>
        </div>
        <Can permission="documents.delete">
          <Button variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" /> Delete
          </Button>
        </Can>
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0">
          {draft && (
            <InformationTypeFields
              draft={draft}
              onChange={setDraft}
              canEdit={canEdit}
              typeId={typeId}
            />
          )}
        </div>

        <div className="min-w-0 space-y-5">
          <Panel title="Used by" description="Everything that carries or routes this type.">
            <div className="space-y-4">
              <SetupList items={t.integrationSetups} />
              {t.busGateways.length > 0 && (
                <div className="border-t border-ink-100 pt-3">
                  <MiniTable
                    rows={t.busGateways}
                    rowKey={(g) => g.gatewayId}
                    empty=""
                    columns={[
                      {
                        header: "Bus gateway",
                        truncate: true,
                        cell: (g) => (
                          <Link
                            to={`/bus-gateways/${g.gatewayId}`}
                            className="block truncate font-medium text-ink-800 hover:text-crimson-700 hover:underline"
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

          <Panel title="History">
            <TrailTable entries={t.trail} />
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

      {deleting && (
        <ConfirmDialog
          title="Delete this information type?"
          body={
            <>
              <strong className="font-medium text-ink-800">{t.code ?? t.name}</strong> and its promoted
              properties will be gone for good. Types still used by integrations can't be deleted.
            </>
          }
          confirmLabel="Delete information type"
          onConfirm={async () => {
            await api.deleteInformationType(typeId);
            void queryClient.invalidateQueries({ queryKey: ["information-types"] });
            navigate("/information-types");
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}
