import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, type InformationTypeRow } from "../../api";
import { withReturn, type ReturnContext } from "../../lib/returnTo";
import { useSessionCan } from "../../auth/guards";
import { SearchSelect } from "../ui/SearchSelect";
import { useIntegrationsCache } from "./shared";

/*
 * Pick-one controls used inside flows. Creating (or opening) an entity is a
 * routed detour: the "New …" link and "View" navigate to the entity's own page
 * carrying a return context; the flow's draft survives in sessionStorage and
 * the pick comes back via `?picked=`.
 *
 * These were lists of one card per candidate. That is fine with six partners
 * and unusable with six hundred, so they are typeaheads now — the same
 * `SearchSelect` the rest of the app uses, which still shows a code and a hint
 * per row and searches both.
 */

/** The detour links that sit under every picker. */
function PickerLinks({ view, create, createLabel }: { view?: string; create?: string; createLabel: string }) {
  if (!view && !create) return null;
  return (
    <div className="mt-1 flex items-center gap-3">
      {view && (
        <Link to={view} className="text-[13px] font-medium text-crimson-700 hover:underline">
          View
        </Link>
      )}
      {create && (
        <Link to={create} className="text-[13px] font-medium text-crimson-700 hover:underline">
          + {createLabel}
        </Link>
      )}
    </div>
  );
}

export function InfoTypePicker({
  value,
  onChange,
  detourCtx,
  filter,
  id,
}: {
  value: number | null;
  onChange: (id: number) => void;
  /** Where "New information type" / "View" detours return to. */
  detourCtx?: ReturnContext;
  /** Narrow the candidate list, e.g. bus-enabled types only. */
  filter?: (t: InformationTypeRow) => boolean;
  id?: string;
}) {
  const types = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const canCreate = useSessionCan("documents.create");

  const candidates = filter ? (types.data ?? []).filter(filter) : (types.data ?? []);

  return (
    <div>
      <SearchSelect
        id={id}
        aria-label="Information type"
        value={value === null ? "" : String(value)}
        disabled={types.isPending}
        onChange={(v) => v !== "" && onChange(Number(v))}
        placeholder="Pick an information type…"
        options={candidates.map((t) => ({
          value: String(t.id),
          label: t.name,
          code: t.code,
          hint: t.format === "Json" ? "JSON" : "XML",
        }))}
      />
      <PickerLinks
        view={detourCtx && value !== null ? withReturn(`/information-types/${value}`, detourCtx) : undefined}
        create={detourCtx && canCreate ? withReturn("/information-types/new", detourCtx) : undefined}
        createLabel="New information type"
      />
    </div>
  );
}

/**
 * Pick-one GatewayApiCall/BusGateway integration behind an entry point (an API
 * gateway attachment or a bus gateway route). Same routed-detour pattern as the
 * other pickers.
 */
export function IntegrationPicker({
  type,
  informationTypeId,
  value,
  onChange,
  detourCtx,
  triggerHint,
  id,
}: {
  type: "GatewayApiCall" | "BusGateway";
  /** Bus routes only run integrations carrying the gateway's own information type. */
  informationTypeId?: number;
  value: number | null;
  onChange: (id: number) => void;
  detourCtx: ReturnContext;
  /**
   * What the new integration is about to be wired into, e.g.
   * "Acme partner API → Northwind Foods". Passed through to the create page so
   * its Trigger node can name the real thing instead of "a gateway attachment".
   * Only this caller knows it — the picker has the integration, not the entry point.
   */
  triggerHint?: string;
  id?: string;
}) {
  const integrations = useIntegrationsCache();
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const canCreate = useSessionCan("subscriptions.create");

  const candidates = (integrations.data ?? []).filter(
    (s) => s.type === type && (informationTypeId === undefined || s.informationTypeId === informationTypeId),
  );

  const createTo = `/subscriptions/new?type=${type}${
    informationTypeId !== undefined ? `&informationTypeId=${informationTypeId}` : ""
  }${triggerHint ? `&trigger=${encodeURIComponent(triggerHint)}` : ""}`;

  return (
    <div>
      <SearchSelect
        id={id}
        aria-label="Integration"
        value={value === null ? "" : String(value)}
        disabled={integrations.isPending}
        onChange={(v) => v !== "" && onChange(Number(v))}
        placeholder="Pick an integration…"
        options={candidates.map((s) => ({
          value: String(s.id),
          label: s.name,
          hint: `Carries ${infoTypes.data?.find((t) => t.id === s.informationTypeId)?.code ?? "…"}`,
        }))}
      />
      <PickerLinks
        view={value !== null ? withReturn(`/subscriptions/${value}`, detourCtx) : undefined}
        create={canCreate ? withReturn(createTo, detourCtx) : undefined}
        createLabel="New integration"
      />
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
  id,
}: {
  value: number | "none" | null;
  onChange: (value: number | "none") => void;
  allowNone?: boolean;
  noneLabel?: string;
  noneSubtitle?: string;
  excludeIds?: number[];
  detourCtx?: ReturnContext;
  id?: string;
}) {
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  const canCreate = useSessionCan("partners.create");

  const candidates = (partners.data ?? []).filter((p) => !p.isSystem && !excludeIds.includes(p.id));

  // "none" is a real option rather than SearchSelect's clear choice: an empty
  // value has to keep meaning "not decided yet", or a required picker could be
  // submitted without an answer.
  const options = [
    ...(allowNone ? [{ value: "none", label: noneLabel, hint: noneSubtitle }] : []),
    ...candidates.map((p) => ({
      value: String(p.id),
      label: p.name,
      hint: `${p.propertyKeys.length} propert${p.propertyKeys.length === 1 ? "y" : "ies"}`,
    })),
  ];

  return (
    <div>
      <SearchSelect
        id={id}
        aria-label="Partner"
        value={value === null ? "" : String(value)}
        disabled={partners.isPending}
        onChange={(v) => v !== "" && onChange(v === "none" ? "none" : Number(v))}
        placeholder="Pick a partner…"
        options={options}
      />
      <PickerLinks
        view={
          detourCtx && typeof value === "number"
            ? withReturn(`/partners/${value}`, detourCtx)
            : undefined
        }
        create={detourCtx && canCreate ? withReturn("/partners/new", detourCtx) : undefined}
        createLabel="New partner"
      />
    </div>
  );
}
