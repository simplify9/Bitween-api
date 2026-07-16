import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type Schedule } from "../../api";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { AdapterConfig, useAdapterCatalog } from "../../components/config/AdapterConfig";
import { ScheduleEditor } from "../../components/config/ScheduleEditor";
import { InfoTypePicker } from "../../components/config/pickers";
import { StepNav, WizardShell, usePersistentDraft } from "../../components/config/wizard";
import { ReviewRow } from "../api-gateways/AttachPartnerWizard";
import { takePicked, useHereAsReturnTarget } from "../../lib/returnTo";
import { schedulesSummary } from "../../lib/schedules";

type Stage = "basics" | "source" | "pipeline" | "review";
const STEPS = ["Basics", "Source & schedule", "Pipeline", "Review"];

interface Draft {
  stage: Stage;
  name: string;
  informationTypeId: number | null;
  receiverId: string | null;
  receiverProperties: Record<string, string>;
  schedules: Schedule[];
  mapperId: string | null;
  mapperProperties: Record<string, string>;
  handlerId: string | null;
  handlerProperties: Record<string, string>;
  enable: boolean;
}

const EMPTY: Draft = {
  stage: "basics",
  name: "",
  informationTypeId: null,
  receiverId: null,
  receiverProperties: {},
  schedules: [],
  mapperId: null,
  mapperProperties: {},
  handlerId: null,
  handlerProperties: {},
  enable: true,
};

export function NewScheduledJobWizard() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();
  const receivers = useAdapterCatalog("receiver");
  const handlers = useAdapterCatalog("handler");
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });

  const [draft, update, clear] = usePersistentDraft<Draft>("bitween-draft-new-job", EMPTY);

  useEffect(() => {
    const picked = takePicked(params, "infotype");
    if (picked !== null) {
      update({ informationTypeId: picked });
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.delete("picked");
          return next;
        },
        { replace: true },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const create = useMutation({
    mutationFn: () =>
      api.createIntegration({
        type: "Receiving",
        name: draft.name,
        informationTypeId: draft.informationTypeId!,
        receiverId: draft.receiverId,
        receiverProperties: draft.receiverProperties,
        mapperId: draft.mapperId,
        mapperProperties: draft.mapperProperties,
        handlerId: draft.handlerId,
        handlerProperties: draft.handlerProperties,
        schedules: draft.schedules,
        enabled: draft.enable,
      }),
    onSuccess: (created) => {
      clear();
      void queryClient.invalidateQueries();
      navigate(`/subscriptions/${created.id}`);
    },
  });

  const current = STEPS.indexOf(
    draft.stage === "basics" ? "Basics" : draft.stage === "source" ? "Source & schedule" : draft.stage === "pipeline" ? "Pipeline" : "Review",
  );

  const infoTypeLabel = infoTypes.data?.find((t) => t.id === draft.informationTypeId)?.code ?? "—";

  return (
    <WizardShell
      title="New scheduled job"
      subtitle="A receiver that pulls documents in on a schedule and pushes them through a pipeline."
      backTo="/subscriptions?types=scheduled-jobs"
      backLabel="Integrations"
      steps={STEPS}
      current={current}
    >
      {draft.stage === "basics" && (
        <>
          <div className="mb-4 max-w-sm">
            <Field label="Job name" htmlFor="sjw-name">
              <TextInput
                id="sjw-name"
                value={draft.name}
                autoFocus
                placeholder="e.g. Coral orders receiver"
                onChange={(e) => update({ name: e.target.value })}
              />
            </Field>
          </div>
          <p className="mb-2 text-[13px] font-medium text-ink-700">Which information type does it pull in?</p>
          <InfoTypePicker
            value={draft.informationTypeId}
            onChange={(id) => update({ informationTypeId: id })}
            detourCtx={{ to: here, label: "Creating a scheduled job" }}
          />
          <StepNav
            onNext={() => update({ stage: "source" })}
            nextDisabled={draft.name.trim().length < 2 || draft.informationTypeId === null}
          />
        </>
      )}

      {draft.stage === "source" && (
        <>
          <section>
            <h2 className="text-[15px] font-semibold text-ink-900">Source</h2>
            <p className="mb-3 text-[13px] text-ink-500">Where documents are pulled from.</p>
            <AdapterConfig
              kind="receiver"
              adapterId={draft.receiverId}
              properties={draft.receiverProperties}
              onChange={(receiverId, receiverProperties) => update({ receiverId, receiverProperties })}
              disabled={false}
              required
            />
          </section>
          <section className="mt-6">
            <h2 className="text-[15px] font-semibold text-ink-900">Schedule</h2>
            <p className="mb-3 text-[13px] text-ink-500">When the source is checked.</p>
            <ScheduleEditor
              schedules={draft.schedules}
              onChange={(schedules) => update({ schedules })}
              disabled={false}
            />
          </section>
          <StepNav
            onBack={() => update({ stage: "basics" })}
            onNext={() => update({ stage: "pipeline" })}
            nextDisabled={draft.receiverId === null || draft.schedules.length === 0}
          />
        </>
      )}

      {draft.stage === "pipeline" && (
        <>
          <div className="space-y-6">
            <section>
              <h2 className="text-[15px] font-semibold text-ink-900">Transformation</h2>
              <p className="mb-3 text-[13px] text-ink-500">Reshapes each document before delivery. Optional.</p>
              <AdapterConfig
                kind="mapper"
                adapterId={draft.mapperId}
                properties={draft.mapperProperties}
                onChange={(mapperId, mapperProperties) => update({ mapperId, mapperProperties })}
                disabled={false}
                noneLabel="None — documents pass through unchanged"
              />
            </section>
            <section>
              <h2 className="text-[15px] font-semibold text-ink-900">Delivery</h2>
              <p className="mb-3 text-[13px] text-ink-500">Where each document ends up.</p>
              <AdapterConfig
                kind="handler"
                adapterId={draft.handlerId}
                properties={draft.handlerProperties}
                onChange={(handlerId, handlerProperties) => update({ handlerId, handlerProperties })}
                disabled={false}
                required
              />
            </section>
          </div>
          <StepNav
            onBack={() => update({ stage: "source" })}
            onNext={() => update({ stage: "review" })}
            nextDisabled={draft.handlerId === null}
          />
        </>
      )}

      {draft.stage === "review" && (
        <>
          <h2 className="mb-3 text-[15px] font-semibold text-ink-900">Review</h2>
          <dl className="space-y-2.5 text-sm">
            <ReviewRow label="Job" value={draft.name} />
            <ReviewRow label="Pulls in" value={infoTypeLabel} />
            <ReviewRow label="Source" value={receivers.data?.find((a) => a.id === draft.receiverId)?.label ?? "—"} />
            <ReviewRow label="Schedule" value={schedulesSummary(draft.schedules)} />
            <ReviewRow label="Delivery" value={handlers.data?.find((a) => a.id === draft.handlerId)?.label ?? "—"} />
          </dl>
          <div className="mt-4">
            <Checkbox
              label="Enable immediately"
              description="Unchecked, the job is created disabled and never runs until enabled."
              checked={draft.enable}
              onChange={(e) => update({ enable: e.target.checked })}
            />
          </div>
          <StepNav
            onBack={() => update({ stage: "pipeline" })}
            onNext={() => create.mutate()}
            nextLabel="Create scheduled job"
            busy={create.isPending}
            error={create.error?.message}
          />
        </>
      )}
    </WizardShell>
  );
}
