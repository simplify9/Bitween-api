import { useMemo, useState } from "react";
import { Link, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSession } from "../../auth/useSession";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Select } from "../../components/ui/forms";
import NativeMapperEditor from "../../components/nativeMapper/NativeMapperEditor";
import { MapperEditorServicesContext, type MapperEditorServices } from "../../components/nativeMapper/editorServices";
import { NATIVE_MAPPER_ID } from "../../lib/nativeMapper/types";
import { formatDateTime } from "../../lib/dates";

/**
 * One place's mapping, for a partner's person: ours until they save their own, which is then used
 * for their exchanges only. The mapping editor runs against the portal's endpoints, with their own
 * partner fixed and no global values.
 */
export function PortalMappingPage() {
  const id = Number(useParams().subscriptionId);
  const queryClient = useQueryClient();
  const { session } = useSession();
  const canEdit = useSessionCan("portal-mappings.edit");
  const canSamples = useSessionCan("portal-exchange-files.view");
  const [editing, setEditing] = useState(false);
  const [sample, setSample] = useState<string | null>(null);

  const mapping = useQuery({ queryKey: keys.portal.mapping(id), queryFn: () => api.getPortalMapping(id) });
  const samples = useQuery({
    queryKey: keys.portal.samples(id),
    queryFn: () => api.listPortalMappingSamples(id),
    enabled: canEdit && canSamples,
  });

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: keys.portal.mapping(id) });
    void queryClient.invalidateQueries({ queryKey: keys.portal.mappings });
  };
  const save = useMutation({
    mutationFn: (properties: Record<string, string>) => api.savePortalMapping(id, properties),
    onSuccess: refresh,
  });
  const reset = useMutation({ mutationFn: () => api.resetPortalMapping(id), onSuccess: refresh });
  const withdraw = useMutation({ mutationFn: () => api.withdrawPortalMapping(id), onSuccess: refresh });
  const loadSample = useMutation({
    mutationFn: (xchangeId: string) => api.getPortalMappingSample(xchangeId),
    onSuccess: setSample,
  });

  const services = useMemo<MapperEditorServices>(
    () => ({
      previewMappingRules: ({ mappingRules, sourceDocument }) =>
        api.previewPortalMapping({ subscriptionId: id, mappingRules, sourceDocument }),
      partnerPropertyKeys: () => api.listPortalPropertyKeys(),
      listValueSets: () => Promise.resolve([]),
      choosePartner: false,
      ai: false,
      globals: false,
    }),
    [id],
  );

  if (mapping.isPending) return <LoadingBlock />;
  if (mapping.isError) return <LoadError error={mapping.error} what="this mapping" onRetry={() => void mapping.refetch()} />;

  const m = mapping.data;
  const theirs = m.theirs !== null;
  const waiting = m.waiting;
  // Their waiting change when there is one, then theirs, then ours, with any sample they just picked.
  const properties = {
    ...(waiting?.theirs ?? m.theirs ?? m.default ?? {}),
    ...(sample !== null ? { SourceSample: sample } : {}),
  };
  const current = theirs && !m.setAside ? "yours" : "ours";
  // The latest of a save or a reset, whichever happened last.
  const outcome = [save, reset]
    .filter((x) => x.isSuccess && !x.isPending)
    .sort((a, b) => b.submittedAt - a.submittedAt)[0]?.data;

  return (
    <div className="min-h-screen bg-canvas">
      <header className="flex items-center gap-3 border-b border-ink-200 bg-white px-4 py-2.5 sm:px-6">
        <Link to="/portal" className="text-ink-500 hover:text-ink-900" title="Back to the portal">
          <ArrowLeft className="size-4" />
          <span className="sr-only">Back to the portal</span>
        </Link>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-[15px] font-semibold text-ink-900">{m.subscriptionName}</h1>
          <p className="text-xs text-ink-500">{session?.partner?.name} · Mapping</p>
        </div>
      </header>

      <main className="mx-auto max-w-3xl space-y-4 px-4 py-6 sm:px-6">
        <section className="space-y-3 rounded-xl border border-ink-200 bg-white p-5">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-[15px] font-semibold text-ink-900">What you receive</h2>
            {m.setAside ? (
              <Badge tone="warn" title="Yours reads an internal value our mapping no longer sends, so ours is used until you change yours.">
                Yours is set aside
              </Badge>
            ) : theirs ? (
              <Badge tone="ok" title="Your own mapping is used for your exchanges. Nobody else's change.">
                Your own mapping
              </Badge>
            ) : (
              <Badge title="You get the same mapping as everyone on this subscription.">Our mapping</Badge>
            )}
            {waiting && (
              <Badge tone="warn" title="Your latest change isn't used until we approve it.">
                Waiting for approval
              </Badge>
            )}
          </div>

          {!m.customizable ? (
            <p className="text-sm text-ink-600">
              This mapping can't be changed here. Ask us if you need what you receive shaped differently.
            </p>
          ) : !canEdit ? (
            <p className="text-sm text-ink-600">Your role can see this but not change it.</p>
          ) : (
            <>
              {m.setAside && (
                <p className="rounded-lg bg-warn-100 px-3 py-2 text-sm text-ink-700">
                  Your mapping reads an internal value our mapping no longer sends, so you get ours for now. Edit yours
                  to remove that field, and it's used again.
                </p>
              )}
              {waiting ? (
                <div className="flex flex-wrap items-center gap-2 rounded-lg bg-warn-100 px-3 py-2 text-sm text-ink-700">
                  <span className="min-w-0 flex-1">
                    {waiting.theirs === null
                      ? "You asked to go back to ours"
                      : "Your change"}{" "}
                    from {formatDateTime(waiting.requestedOn)} waits for our approval. You keep getting {current} until
                    then.
                  </span>
                  <Button
                    busy={withdraw.isPending}
                    onClick={() => withdraw.mutate()}
                    title="Take your change back before we decide on it. Nothing else changes."
                  >
                    Take it back
                  </Button>
                </div>
              ) : (
                m.rejected && (
                  <p className="rounded-lg bg-ink-100 px-3 py-2 text-sm text-ink-700">
                    We didn't approve your last change ({formatDateTime(m.rejected.decidedOn)})
                    {m.rejected.note ? `: ${m.rejected.note}` : "."} You keep getting {current}.
                  </p>
                )
              )}
              <p className="text-sm text-ink-600">
                {theirs
                  ? "You shape what you receive here, for you only."
                  : "Make your own version to shape what you receive. It starts as a copy of ours and is used for you only."}{" "}
                {m.needsApproval
                  ? "Your changes wait for our approval before they're used."
                  : "Changes are used from your next exchange."}
              </p>

              {canSamples && (samples.data?.length ?? 0) > 0 && (
                <div className="flex flex-wrap items-center gap-2">
                  <div className="w-72">
                    <Select
                      aria-label="Sample"
                      title="Preview against what one of your recent exchanges received. Replaces the sample in the editor."
                      defaultValue=""
                      onChange={(e) => e.target.value && loadSample.mutate(e.target.value)}
                      options={[
                        { value: "", label: "Use a recent exchange as the sample…" },
                        ...(samples.data ?? []).map((s) => ({
                          value: s.xchangeId,
                          label: `${formatDateTime(s.startedOn)}${s.inputName ? ` · ${s.inputName}` : ""}`,
                        })),
                      ]}
                    />
                  </div>
                  {sample !== null && <span className="text-[13px] text-ink-500">Sample loaded.</span>}
                </div>
              )}

              <div className="flex flex-wrap gap-2">
                <Button variant="primary" onClick={() => setEditing(true)}>
                  {waiting?.theirs ? "Edit your waiting change" : theirs ? "Edit your mapping" : "Make your own version"}
                </Button>
                {theirs && waiting?.theirs !== null && (
                  <Button
                    busy={reset.isPending}
                    onClick={() => reset.mutate()}
                    title={
                      m.needsApproval
                        ? "Ask to drop your own mapping and get ours again, once we approve it"
                        : "Delete your own mapping and get ours again, from your next exchange"
                    }
                  >
                    Go back to ours
                  </Button>
                )}
              </div>
              {/* A save's failure shows in the editor, where the save was made. */}
              <FormError>{(reset.error ?? withdraw.error ?? loadSample.error)?.message}</FormError>
              {/* A change sent for approval says so in the notice above. */}
              {outcome && !outcome.waiting && (
                <p className="text-[13px] text-ok-700">Saved. Used from your next exchange.</p>
              )}
            </>
          )}
        </section>
      </main>

      {editing && (
        <MapperEditorServicesContext.Provider value={services}>
          <NativeMapperEditor
            key={sample ?? "saved"}
            target={{
              kind: "draft",
              mapperId: NATIVE_MAPPER_ID,
              mapperProperties: properties,
              partnerId: session?.partner ? Number(session.partner.id) : null,
              onSave: async (mapperProperties) => {
                await save.mutateAsync(mapperProperties);
              },
            }}
            onClose={() => setEditing(false)}
          />
        </MapperEditorServicesContext.Provider>
      )}
    </div>
  );
}
