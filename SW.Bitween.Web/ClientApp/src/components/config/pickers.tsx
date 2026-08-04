import { useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { api, type InformationTypeRow } from "../../api";
import { useSessionCan } from "../../auth/guards";
import { SearchSelect } from "../ui/SearchSelect";
import { InformationTypeDialog } from "./InformationTypeDialog";
import { IntegrationDialog } from "./IntegrationDialog";
import { PartnerDialog } from "./PartnerDialog";
import { useIntegrationsCache } from "./shared";

/*
 * Pick-one controls used inside flows. Creating or amending the thing you are
 * picking happens in a dialog, right here — no page change, so nothing has to be
 * remembered across a trip and no draft has to survive one. That replaced a routed
 * detour (`?return=` + sessionStorage + `?picked=`), which is why none of these
 * take a return context any more.
 *
 * They were lists of one card per candidate. That is fine with six partners and
 * unusable with six hundred, so they are typeaheads — the same `SearchSelect` the
 * rest of the app uses, which shows a code and a hint per row and searches both.
 */

/** The links that sit under a picker. Routed ones navigate; `onAct` ones open in place. */
function PickerLinks({
  view,
  create,
  createLabel,
  actions = [],
}: {
  view?: string;
  create?: string;
  createLabel: string;
  actions?: { label: string; icon?: boolean; onAct: () => void }[];
}) {
  if (!view && !create && actions.length === 0) return null;
  return (
    <div className="mt-1 flex flex-wrap items-center gap-3">
      {view && (
        <Link to={view} className="text-[13px] font-medium text-crimson-700 hover:underline">
          View
        </Link>
      )}
      {actions.map((a) => (
        <button
          key={a.label}
          type="button"
          onClick={a.onAct}
          className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline"
        >
          {a.icon && <Plus className="size-3" />}
          {a.label}
        </button>
      ))}
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
  filter,
  busRequired = false,
  id,
}: {
  value: number | null;
  onChange: (id: number) => void;
  /** Narrow the candidate list, e.g. bus-enabled types only. */
  filter?: (t: InformationTypeRow) => boolean;
  /** Anything created here must be on the bus, because this flow reaches it that way. */
  busRequired?: boolean;
  id?: string;
}) {
  const types = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const canCreate = useSessionCan("documents.create");
  const canEdit = useSessionCan("documents.edit");
  /** undefined = closed, null = creating, number = editing that type. */
  const [dialog, setDialog] = useState<number | null | undefined>(undefined);

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
        createLabel="New information type"
        actions={[
          ...(canEdit && value !== null ? [{ label: "Edit it", onAct: () => setDialog(value) }] : []),
          ...(canCreate
            ? [{ label: "New information type", icon: true, onAct: () => setDialog(null) }]
            : []),
        ]}
      />
      {dialog !== undefined && (
        <InformationTypeDialog
          typeId={dialog}
          busRequired={busRequired}
          onClose={() => setDialog(undefined)}
          onSaved={(type) => onChange(type.id)}
        />
      )}
    </div>
  );
}

/**
 * Pick-one GatewayApiCall/BusGateway integration behind an entry point (an API
 * gateway attachment or a bus gateway route). Creating one opens a dialog.
 */
export function IntegrationPicker({
  type,
  informationTypeId,
  value,
  onChange,
  id,
}: {
  type: "GatewayApiCall" | "BusGateway";
  /** Bus routes only run integrations carrying the gateway's own information type. */
  informationTypeId?: number;
  value: number | null;
  onChange: (id: number) => void;
  id?: string;
}) {
  const integrations = useIntegrationsCache();
  const infoTypes = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });
  const canCreate = useSessionCan("subscriptions.create");
  const [creating, setCreating] = useState(false);

  const candidates = (integrations.data ?? []).filter(
    (s) => s.type === type && (informationTypeId === undefined || s.informationTypeId === informationTypeId),
  );

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
      {/* An integration keeps a "View": its page holds run history and traffic that
          no dialog is going to show, and going there is the user's own choice. */}
      <PickerLinks
        view={value !== null ? `/subscriptions/${value}` : undefined}
        createLabel="New integration"
        actions={canCreate ? [{ label: "New integration", icon: true, onAct: () => setCreating(true) }] : []}
      />
      {creating && (
        <IntegrationDialog
          type={type}
          {...(informationTypeId !== undefined ? { informationTypeId } : {})}
          onClose={() => setCreating(false)}
          onCreated={onChange}
        />
      )}
    </div>
  );
}

/**
 * Pick a partner — and create or amend one without leaving.
 *
 * `detourCtx` is gone: a partner is edited in a dialog now, so there is no page to
 * come back from and no draft to persist across the trip.
 */
export function PartnerPicker({
  value,
  onChange,
  allowNone = false,
  noneLabel = "No partner",
  noneSubtitle,
  excludeIds = [],
  id,
}: {
  value: number | "none" | null;
  onChange: (value: number | "none") => void;
  allowNone?: boolean;
  noneLabel?: string;
  noneSubtitle?: string;
  excludeIds?: number[];
  id?: string;
}) {
  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  const canCreate = useSessionCan("partners.create");
  const canEdit = useSessionCan("partners.edit");
  /** undefined = closed, null = creating, number = editing that partner. */
  const [dialog, setDialog] = useState<number | null | undefined>(undefined);

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
        createLabel="New partner"
        actions={[
          ...(canEdit && typeof value === "number"
            ? [{ label: "Edit it", onAct: () => setDialog(value) }]
            : []),
          ...(canCreate ? [{ label: "New partner", icon: true, onAct: () => setDialog(null) }] : []),
        ]}
      />
      {dialog !== undefined && (
        <PartnerDialog
          partnerId={dialog}
          onClose={() => setDialog(undefined)}
          onSaved={(partnerId) => onChange(partnerId)}
        />
      )}
    </div>
  );
}
