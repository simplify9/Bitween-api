import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronUp, Plus, X } from "lucide-react";
import { api, type SubscriptionType } from "../../../api";
import { useSessionCan } from "../../../auth/guards";
import { Badge } from "../../../components/ui/basics";
import { Field, TextInput } from "../../../components/ui/forms";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { AdapterConfig } from "../../../components/config/AdapterConfig";
import { MatchExpressionEditor } from "../../../components/config/MatchExpressionEditor";
import { HealthBadge } from "../../../components/config/shared";
import { ResponseFields } from "../../subscriptions/studio/ResponseFields";
import { keys } from "../../../api/queryKeys";
import {
  BUS_NODES,
  type BusNodeId,
  type SubscriptionDraft,
  type RouteDraft,
} from "./model";

/**
 * The configuration surface: one node's form, docked under the canvas.
 *
 * Docked rather than a drawer over the diagram — the whole point of the canvas is
 * seeing where you are while you edit. It hides to give the canvas the screen,
 * but never while it holds unsaved edits: a panel that can conceal a half-finished
 * handler is how an operator loses work, which is why the drawer was rejected in
 * the first place.
 */
export function Inspector({
  node,
  dirty,
  collapsed,
  onToggleCollapsed,
  onClose,
  children,
}: {
  node: BusNodeId | null;
  dirty: boolean;
  collapsed: boolean;
  onToggleCollapsed: () => void;
  onClose: () => void;
  children: React.ReactNode;
}) {
  if (node === null)
    return (
      <div className="shrink-0 border-t border-ink-200 bg-white px-4 py-2.5">
        <p className="text-[13px] text-ink-400">
          Pick a node on the canvas to configure it. Everything about this route — its filter, its
          partner, and the subscription it runs — is edited here.
        </p>
      </div>
    );

  const { label, description, icon: Icon } = BUS_NODES[node];

  return (
    <div className="flex max-h-[55vh] shrink-0 flex-col border-t border-ink-200 bg-white">
      <div className="flex shrink-0 items-center gap-2.5 border-b border-ink-100 px-4 py-2.5">
        <span className="grid size-7 shrink-0 place-items-center rounded-lg bg-crimson-600 text-white">
          <Icon className="size-4" aria-hidden />
        </span>
        <div className="min-w-0 flex-1">
          <h2 className="flex items-center gap-2 text-[14px] font-semibold text-ink-900">
            {label}
            {dirty && <Badge tone="warn">Unsaved</Badge>}
          </h2>
          <p className="truncate text-[12px] text-ink-500">{description}</p>
        </div>
        <button
          type="button"
          onClick={onToggleCollapsed}
          disabled={dirty && !collapsed}
          title={
            dirty && !collapsed
              ? "Save or discard this step's changes first — hiding it would put them out of sight."
              : collapsed
                ? "Show this step"
                : "Hide this step and give the canvas the screen"
          }
          aria-label={collapsed ? "Show this step" : "Hide this step"}
          className="shrink-0 rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700 disabled:cursor-not-allowed disabled:opacity-40"
        >
          {collapsed ? <ChevronUp className="size-4" /> : <ChevronDown className="size-4" />}
        </button>
        <button
          type="button"
          onClick={onClose}
          aria-label="Close this step"
          title="Close  Esc"
          className="shrink-0 rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
        >
          <X className="size-4" />
        </button>
      </div>
      {!collapsed && <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">{children}</div>}
    </div>
  );
}

/** The route itself: its filter, whose values it runs with, and what it runs. */
export function RouteBody({
  draft,
  onChange,
  promotedProperties,
  informationTypeId,
  informationTypeCode,
  disabled,
  onNewPartner,
  onEditPartner,
  onNewSubscription,
}: {
  draft: RouteDraft;
  onChange: (patch: Partial<RouteDraft>) => void;
  promotedProperties: { key: string; path: string }[];
  /** Routes can only run subscriptions carrying the gateway's own type. */
  informationTypeId: number;
  informationTypeCode: string;
  disabled: boolean;
  onNewPartner: () => void;
  /** Opens the chosen partner's values here, rather than sending you to its page. */
  onEditPartner: (partnerId: number) => void;
  onNewSubscription: () => void;
}) {
  const partners = useQuery({ queryKey: keys.partners.list, queryFn: () => api.listPartners() });
  const subscriptions = useQuery({
    queryKey: keys.subscriptions.cache,
    queryFn: () => api.listSubscriptions(),
  });
  const canCreatePartner = useSessionCan("partners.create");
  const canCreateSubscription = useSessionCan("subscriptions.create");

  const candidates = (subscriptions.data ?? []).filter(
    (s) => s.type === "BusGateway" && s.informationTypeId === informationTypeId,
  );

  return (
    <div className="space-y-5">
      <Field
        label="Filter"
        hint={`Which ${informationTypeCode} messages this route picks up. No filter picks up every one of them.`}
      >
        <MatchExpressionEditor
          value={draft.matchExpression}
          onChange={(matchExpression) => onChange({ matchExpression })}
          properties={promotedProperties}
          disabled={disabled}
        />
      </Field>

      <div className="grid max-w-4xl gap-5 sm:grid-cols-2">
        <Field
          label="Partner"
          htmlFor="bs-partner"
          hint="Whose {{partner.…}} values the adapters resolve against."
        >
          <SearchSelect
            id="bs-partner"
            aria-label="Partner"
            value={draft.partner === "none" ? "none" : String(draft.partner)}
            disabled={disabled || partners.isPending}
            onChange={(v) => onChange({ partner: v === "none" ? "none" : Number(v) })}
            options={[
              { value: "none", label: "No partner", hint: "Runs without {{partner.…}} values" },
              ...(partners.data ?? [])
                .filter((p) => !p.isSystem)
                .map((p) => ({
                  value: String(p.id),
                  label: p.name,
                  hint: `${p.propertyKeys.length} propert${p.propertyKeys.length === 1 ? "y" : "ies"}`,
                })),
            ]}
          />
          <PanelLinks
            onEdit={
              typeof draft.partner === "number" ? () => onEditPartner(draft.partner as number) : undefined
            }
            editLabel="Edit its values"
            onCreate={canCreatePartner && !disabled ? onNewPartner : undefined}
            createLabel="New partner"
          />
        </Field>

        <Field
          label="Runs the subscription"
          htmlFor="bs-subscription"
          hint={`Only subscriptions carrying ${informationTypeCode} can run here.`}
        >
          <SearchSelect
            id="bs-subscription"
            aria-label="Subscription"
            value={draft.subscriptionId === null ? "" : String(draft.subscriptionId)}
            disabled={disabled || subscriptions.isPending}
            placeholder="Pick a subscription…"
            onChange={(v) => v !== "" && onChange({ subscriptionId: Number(v) })}
            options={candidates.map((s) => ({ value: String(s.id), label: s.name }))}
          />
          <PanelLinks
            view={draft.subscriptionId !== null ? `/subscriptions/${draft.subscriptionId}` : undefined}
            onCreate={canCreateSubscription && !disabled ? onNewSubscription : undefined}
            createLabel="New subscription"
          />
        </Field>
      </div>
    </div>
  );
}

/** Subscription-level facts: name, whether it runs, and the lane and policy it runs under. */
export function SubscriptionBody({
  draft,
  onChange,
  disabled,
  health,
  lastException,
  /** Set while the subscription is being defined here — the name is the first thing asked. */
  autoFocusName = false,
}: {
  draft: SubscriptionDraft;
  onChange: (patch: Partial<SubscriptionDraft>) => void;
  disabled: boolean;
  health: { isRunning: boolean; consecutiveFailures: number } | null;
  lastException: string | null;
  autoFocusName?: boolean;
}) {
  const workGroups = useQuery({
    queryKey: keys.workGroups.list,
    queryFn: () => api.listWorkGroups(),
  });
  const retryPolicies = useQuery({ queryKey: keys.retryPolicies.list, queryFn: () => api.listRetryPolicies() });

  return (
    <div className="space-y-4">
      <div className="grid max-w-5xl gap-5 sm:grid-cols-2 lg:grid-cols-4">
        <Field label="Name" htmlFor="bs-int-name">
          <TextInput
            id="bs-int-name"
            value={draft.name}
            disabled={disabled}
            autoFocus={autoFocusName}
            placeholder="e.g. Coral orders to SAP"
            onChange={(e) => onChange({ name: e.target.value })}
          />
        </Field>
        <Field label="Runs at all" hint="Disabled, no route ever reaches it.">
          <button
            type="button"
            disabled={disabled}
            onClick={() => onChange({ enabled: !draft.enabled })}
            className={`h-9.5 rounded-lg px-3 text-sm font-medium disabled:cursor-not-allowed ${
              draft.enabled ? "bg-ok-100 text-ok-600" : "bg-ink-100 text-ink-700"
            }`}
          >
            {draft.enabled ? "Active" : "Disabled"}
          </button>
        </Field>
        <Field label="Work group" htmlFor="bs-int-wg" hint="Which queue lane its messages wait in.">
          <SearchSelect
            id="bs-int-wg"
            value={draft.workGroupId === null ? "" : String(draft.workGroupId)}
            disabled={disabled}
            onChange={(v) => onChange({ workGroupId: v === "" ? null : Number(v) })}
            clearLabel="Ungrouped (default lane)"
            options={(workGroups.data ?? []).map((w) => ({ value: String(w.id), label: w.name }))}
          />
        </Field>
        <Field label="Retry policy" htmlFor="bs-int-rp">
          <SearchSelect
            id="bs-int-rp"
            value={draft.retryPolicyId === null ? "" : String(draft.retryPolicyId)}
            disabled={disabled}
            onChange={(v) => onChange({ retryPolicyId: v === "" ? null : Number(v) })}
            clearLabel="None — failures are not retried"
            options={(retryPolicies.data ?? []).map((p) => ({ value: String(p.id), label: p.name }))}
          />
        </Field>
      </div>

      {health && (
        <p className="flex items-center gap-2 text-[13px] text-ink-500">
          Right now: <HealthBadge {...health} />
        </p>
      )}
      {lastException && (
        <pre className="max-h-32 overflow-auto rounded-xl border border-danger-200 bg-danger-50 px-4 py-3 font-mono text-[11px] leading-relaxed whitespace-pre-wrap text-danger-800">
          {lastException}
        </pre>
      )}
    </div>
  );
}

export function TransformationBody({
  draft,
  onChange,
  disabled,
  mapperEditorHref,
}: {
  draft: SubscriptionDraft;
  onChange: (patch: Partial<SubscriptionDraft>) => void;
  disabled: boolean;
  mapperEditorHref?: string | null;
}) {
  return (
    <AdapterConfig
      kind="mapper"
      adapterId={draft.mapperId}
      properties={draft.mapperProperties}
      onChange={(mapperId, mapperProperties) => onChange({ mapperId, mapperProperties })}
      disabled={disabled}
      noneLabel="None — the message passes through unchanged"
      mapperEditorHref={mapperEditorHref}
    />
  );
}

export function DeliveryBody({
  draft,
  onChange,
  disabled,
}: {
  draft: SubscriptionDraft;
  onChange: (patch: Partial<SubscriptionDraft>) => void;
  disabled: boolean;
}) {
  return (
    <AdapterConfig
      kind="handler"
      adapterId={draft.handlerId}
      properties={draft.handlerProperties}
      onChange={(handlerId, handlerProperties) => onChange({ handlerId, handlerProperties })}
      disabled={disabled}
      noneLabel="None — the message stops here"
    />
  );
}

export function ResponseBody({
  draft,
  onChange,
  disabled,
  candidates,
}: {
  draft: SubscriptionDraft;
  onChange: (patch: Partial<SubscriptionDraft>) => void;
  disabled: boolean;
  candidates: { id: number; name: string; type: SubscriptionType }[];
}) {
  return (
    <div className="space-y-3">
      <ResponseFields
        handlerId={draft.handlerId}
        responseSubscriptionId={draft.responseSubscriptionId}
        responseMessageTypeName={draft.responseMessageTypeName}
        onChange={onChange}
        disabled={disabled}
        candidates={candidates}
        idPrefix="bs-resp"
      />
      {draft.handlerId !== null && (
        <p className="text-[12px] text-ink-500">
          A bus message is a fan-out — every route bound to that message's information type picks
          it up, on this gateway and any other.
        </p>
      )}
    </div>
  );
}

/** The links under a picker. Everything here acts in place; nothing navigates away. */
function PanelLinks({
  view,
  onEdit,
  editLabel = "Edit",
  onCreate,
  createLabel,
}: {
  view?: string;
  onEdit?: () => void;
  editLabel?: string;
  onCreate?: () => void;
  createLabel: string;
}) {
  if (!view && !onEdit && !onCreate) return null;
  return (
    <div className="mt-1 flex items-center gap-3">
      {view && (
        <Link to={view} className="text-[13px] font-medium text-crimson-700 hover:underline">
          View
        </Link>
      )}
      {onEdit && (
        <button
          type="button"
          onClick={onEdit}
          className="text-[13px] font-medium text-crimson-700 hover:underline"
        >
          {editLabel}
        </button>
      )}
      {onCreate && (
        <button
          type="button"
          onClick={onCreate}
          className="inline-flex items-center gap-1 text-[13px] font-medium text-crimson-700 hover:underline"
        >
          <Plus className="size-3" /> {createLabel}
        </button>
      )}
    </div>
  );
}
