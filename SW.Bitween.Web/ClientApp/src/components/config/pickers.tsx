import { useQuery } from "@tanstack/react-query";
import { api, type InformationTypeRow } from "../../api";
import { withReturn, type ReturnContext } from "../../lib/returnTo";
import { useSessionCan } from "../../auth/guards";
import { LoadingBlock } from "../ui/basics";
import { CodeBadge } from "../ui/Panel";
import { useIntegrationsCache } from "./shared";
import { CreateLinkCard, OptionCard } from "./wizard";

/*
 * Pick-one lists used inside flows. Creating (or opening) an entity is a
 * routed detour: the "New …" card and the per-option edit icon navigate to
 * the entity's own page carrying a return context; the flow's draft
 * survives in sessionStorage and the pick comes back via `?picked=`.
 */

export function InfoTypePicker({
  value,
  onChange,
  detourCtx,
  filter,
}: {
  value: number | null;
  onChange: (id: number) => void;
  /** Where "New information type" / per-option edit detours return to. */
  detourCtx?: ReturnContext;
  /** Narrow the candidate list, e.g. bus-enabled types only. */
  filter?: (t: InformationTypeRow) => boolean;
}) {
  const types = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const canCreate = useSessionCan("documents.create");

  if (types.isPending) return <LoadingBlock label="Loading information types…" />;

  const candidates = filter ? (types.data ?? []).filter(filter) : (types.data ?? []);

  return (
    <div className="space-y-2">
      {candidates.map((t) => (
        <OptionCard
          key={t.id}
          selected={value === t.id}
          onSelect={() => onChange(t.id)}
          title={t.name}
          subtitle={t.format === "Json" ? "JSON" : "XML"}
          right={<CodeBadge code={t.code} name={t.name} />}
          editHref={detourCtx ? withReturn(`/information-types/${t.id}`, detourCtx) : undefined}
        />
      ))}
      {detourCtx && canCreate && (
        <CreateLinkCard
          to={withReturn("/information-types/new", detourCtx)}
          title="New information type"
          subtitle="Create it on its own page — then continue right here."
        />
      )}
    </div>
  );
}

/**
 * Pick-one list of GatewayApiCall/BusGateway integrations behind an entry
 * point (an API gateway attachment or a bus gateway route). Same
 * routed-detour pattern as the other pickers.
 */
export function IntegrationPicker({
  type,
  informationTypeId,
  value,
  onChange,
  detourCtx,
}: {
  type: "GatewayApiCall" | "BusGateway";
  /** Bus routes only run integrations carrying the gateway's own information type. */
  informationTypeId?: number;
  value: number | null;
  onChange: (id: number) => void;
  detourCtx: ReturnContext;
}) {
  const integrations = useIntegrationsCache();
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const canCreate = useSessionCan("subscriptions.create");

  const candidates = (integrations.data ?? []).filter(
    (s) => s.type === type && (informationTypeId === undefined || s.informationTypeId === informationTypeId),
  );

  const createTo = `/subscriptions/new?type=${type}${
    informationTypeId !== undefined ? `&informationTypeId=${informationTypeId}` : ""
  }`;

  return (
    <div className="space-y-2">
      {candidates.map((s) => (
        <OptionCard
          key={s.id}
          selected={value === s.id}
          onSelect={() => onChange(s.id)}
          title={s.name}
          subtitle={`Carries ${infoTypes.data?.find((t) => t.id === s.informationTypeId)?.code ?? "…"}`}
          editHref={withReturn(`/subscriptions/${s.id}`, detourCtx)}
        />
      ))}
      {canCreate && (
        <CreateLinkCard
          to={withReturn(createTo, detourCtx)}
          title="New integration"
          subtitle="Define its pipeline on its own page — then continue right here."
        />
      )}
    </div>
  );
}

export function PartnerPicker({
  value,
  onChange,
  allowNone = false,
  noneLabel = "No partner",
  noneSubtitle,
  excludeIds = [],
  detourCtx,
}: {
  value: number | "none" | null;
  onChange: (value: number | "none") => void;
  allowNone?: boolean;
  noneLabel?: string;
  noneSubtitle?: string;
  excludeIds?: number[];
  detourCtx?: ReturnContext;
}) {
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  const canCreate = useSessionCan("partners.create");

  if (partners.isPending) return <LoadingBlock label="Loading partners…" />;

  const candidates = (partners.data ?? []).filter((p) => !p.isSystem && !excludeIds.includes(p.id));

  return (
    <div className="space-y-2">
      {allowNone && (
        <OptionCard
          selected={value === "none"}
          onSelect={() => onChange("none")}
          title={noneLabel}
          subtitle={noneSubtitle}
        />
      )}
      {candidates.map((p) => (
        <OptionCard
          key={p.id}
          selected={value === p.id}
          onSelect={() => onChange(p.id)}
          title={p.name}
          subtitle={`${Object.keys(p.adapterProperties).length} propert${Object.keys(p.adapterProperties).length === 1 ? "y" : "ies"}`}
          editHref={detourCtx ? withReturn(`/partners/${p.id}`, detourCtx) : undefined}
        />
      ))}
      {detourCtx && canCreate && (
        <CreateLinkCard
          to={withReturn("/partners/new", detourCtx)}
          title="New partner"
          subtitle="Create it on its own page — then continue right here."
        />
      )}
    </div>
  );
}
