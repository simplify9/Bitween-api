import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Plug, Send, Trash2 } from "lucide-react";
import { api, type AdapterInfo, type AdapterProp, type NotificationChannelDetail } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { useLeaveGuard } from "../../lib/useLeaveGuard";
import { usePageTitle } from "../../lib/pageTitle";
import { AdapterConfig, PropField } from "../../components/config/AdapterConfig";
import { useAdapterCatalog } from "../../components/config/adapterCatalog";
import { HistoryCard } from "../../components/config/HistoryCard";
import { BackLink } from "../../components/ui/BackLink";
import { FieldProblem, StillNeeds, useFormCheck, type FormNeed } from "../../components/ui/formCheck";
import { Field, TextInput } from "../../components/ui/forms";
import { Badge, Button, EmptyState, FormError, LoadingBlock, ProblemText } from "../../components/ui/basics";
import { ExceptionLine } from "../../components/ui/Exception";
import { EditableTitle, Panel, UnsavedBar } from "../../components/ui/Panel";
import { ConfirmDialog } from "../../components/ui/overlays";
import { MoreActionsMenu } from "../../components/layout/HeaderActions";
import { MiniTable } from "../../components/ui/Table";
import { formatDateTime, timeAgo } from "../../lib/dates";
import { returnPath, safeReturn } from "../../lib/detour";
import {
  CHANNEL_TYPES,
  channelTypeOf,
  channelUseHref,
  describeChannelUse,
  type ChannelType,
} from "../../lib/notificationChannels";
import { adapterIncomplete } from "../subscriptions/studio/faces";

interface ChannelDraft {
  name: string;
  handlerId: string | null;
  handlerProperties: Record<string, string>;
}

const EMPTY: ChannelDraft = { name: "", handlerId: null, handlerProperties: {} };

const SETTINGS_SECTION = `/settings?section=${encodeURIComponent("Notification channels")}`;

/** What each main type asks for, with the labels a person would use rather than the adapter's keys. */
const TYPE_FIELDS: Record<string, { prop: AdapterProp; label: string }[]> = {
  NativeEmailMessageHandler: [
    {
      prop: { key: "To", optional: false, secret: false, description: "Addresses, separated by commas." },
      label: "To",
    },
    { prop: { key: "Cc", optional: true, secret: false, description: "Copied in, separated by commas." }, label: "Cc" },
    {
      prop: { key: "Bcc", optional: true, secret: false, description: "Copied in without the others seeing." },
      label: "Bcc",
    },
  ],
  NativeTeamsMessageHandler: [
    {
      prop: {
        key: "WebhookUrl",
        optional: false,
        secret: true,
        description:
          'In Teams, open the channel\'s Workflows, add "Post to a channel when a webhook request is received", and copy the address it gives you. Anyone who has it can post to the channel, so it is kept hidden once saved.',
      },
      label: "Workflows webhook URL",
    },
  ],
  NativeSlackMessageHandler: [
    {
      prop: {
        key: "WebhookUrl",
        optional: false,
        secret: true,
        description:
          "In Slack, add an incoming webhook for the channel (Apps › Incoming Webhooks) and copy its https://hooks.slack.com/services/… address. Anyone who has it can post there, so it is kept hidden once saved.",
      },
      label: "Incoming webhook URL",
    },
  ],
};

/** What a channel can't be saved without, each said at the part of the page it is about. */
function channelNeeds(
  draft: ChannelDraft | null,
  otherChosen: boolean,
  handlers: { data: AdapterInfo[] | undefined },
  isNew: boolean,
): FormNeed<"name" | "type" | "fields" | "adapter">[] {
  if (draft === null) return [];
  const type = channelTypeOf(draft.handlerId);
  const other = type === null && (draft.handlerId !== null || otherChosen);
  const required = type === null ? [] : TYPE_FIELDS[type.adapterId].filter((f) => !f.prop.optional);
  const labels = required.map((f) => f.label).join(" and ");
  return [
    {
      field: "name",
      failing: draft.name.trim() === "",
      needs: "a name",
      message: "Give the channel a name, e.g. Ops email.",
      // A new channel's name is a field of the form; a saved one's is edited in the heading.
      focus: isNew ? "nc-name" : "nc-name-problem",
    },
    {
      field: "type",
      failing: draft.handlerId === null,
      needs: "how it sends",
      message: other ? "Pick the adapter it sends through." : "Pick how it sends: Email, Teams, Slack or another adapter.",
      focus: other ? "nc-adapter-problem" : "nc-type-problem",
    },
    {
      field: "fields",
      failing: required.some((f) => !draft.handlerProperties[f.prop.key]),
      needs: `the ${labels}`,
      message: `Fill in the ${labels}.`,
      focus: "nc-fields-problem",
    },
    {
      field: "adapter",
      failing: other && adapterIncomplete(handlers, draft.handlerId, draft.handlerProperties),
      needs: "the adapter's required fields",
      message: "Fill in the adapter's required fields.",
      focus: "nc-adapter-problem",
    },
  ];
}

/**
 * One notification channel, on its own page: how it sends, where to, a test send, who uses it and
 * what it has sent. Pickers come here to make or change one and are sent back with it picked - see
 * `lib/detour.ts`.
 */
export function NotificationChannelPage({ isNew = false }: { isNew?: boolean }) {
  const { id = "" } = useParams();
  const channelId = isNew ? null : Number(id);
  const [params] = useSearchParams();
  const returnTo = safeReturn(params.get("return"));
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan(isNew ? "notifiers.create" : "notifiers.edit");
  const canDelete = useSessionCan("notifiers.delete");
  const handlers = useAdapterCatalog("handler");

  const existing = useQuery({
    queryKey: keys.notificationChannels.detail(channelId ?? "new"),
    queryFn: () => api.getNotificationChannel(channelId!),
    enabled: channelId !== null,
    retry: false,
  });

  const [draft, setDraft] = useState<ChannelDraft | null>(isNew ? EMPTY : null);
  /** "Use another adapter" was picked, before an adapter has been. */
  const [otherChosen, setOtherChosen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const check = useFormCheck(channelNeeds(draft, otherChosen, handlers, isNew));

  const saved = useMemo<ChannelDraft | null>(
    () =>
      existing.data
        ? { name: existing.data.name, handlerId: existing.data.handlerId, handlerProperties: existing.data.handlerProperties ?? {} }
        : null,
    [existing.data],
  );

  useEffect(() => {
    if (saved && draft === null) setDraft(saved);
  }, [saved, draft]);

  const dirty = !isNew && draft !== null && saved !== null && JSON.stringify(draft) !== JSON.stringify(saved);
  // Save, Cancel and Delete deal with the changes themselves, so they go through `leave`.
  const { leave, dialog: leaveDialog } = useLeaveGuard(dirty);
  usePageTitle(existing.data?.name);

  const save = useMutation({
    mutationFn: async () => {
      const body = { name: draft!.name.trim(), handlerId: draft!.handlerId!, handlerProperties: draft!.handlerProperties };
      if (channelId !== null) {
        await api.updateNotificationChannel(channelId, body);
        return channelId;
      }
      return api.createNotificationChannel(body);
    },
    onSuccess: async (savedId) => {
      await queryClient.invalidateQueries({ queryKey: keys.notificationChannels.all });
      if (isNew) {
        navigate(returnTo ? returnPath(returnTo, savedId, "channel") : `/settings/notification-channels/${savedId}`, {
          replace: true,
        });
      } else if (returnTo) {
        // Came from a picker to change it: done, so back to whatever was being edited there.
        leave(() => navigate(returnTo, { replace: true }));
      } else {
        setDraft(null);
      }
    },
  });

  if (!isNew && existing.isPending) return <LoadingBlock label="Loading channel…" />;
  if (!isNew && existing.isError)
    return (
      <EmptyState title="This channel no longer exists">
        <Link to={SETTINGS_SECTION} className="font-medium link">
          Back to notification channels
        </Link>
      </EmptyState>
    );
  if (draft === null) return <LoadingBlock label="Loading channel…" />;

  const type = channelTypeOf(draft.handlerId);
  const other = type === null && (draft.handlerId !== null || otherChosen);
  const update = (patch: Partial<ChannelDraft>) => setDraft((d) => ({ ...d!, ...patch }));

  const pickType = (next: ChannelType) => {
    setOtherChosen(false);
    if (next.adapterId === draft.handlerId) return;
    // Another type's settings mean nothing here, and a stored secret never follows a change of adapter.
    update({ handlerId: next.adapterId, handlerProperties: {} });
  };
  const pickOther = () => {
    setOtherChosen(true);
    if (type !== null) update({ handlerId: null, handlerProperties: {} });
  };

  const missing = check.missing;

  const back = returnTo ?? SETTINGS_SECTION;

  return (
    <div className="pb-24">
      <BackLink to={back} label={returnTo ? "Back" : "Notification channels"} />

      <div className="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">
            {/* A new channel's name is asked for in the form, labelled; an empty input standing in
                for the heading read as a grey title, not as something to fill in. */}
            {isNew ? (
              draft.name.trim() || "New notification channel"
            ) : (
              <EditableTitle
                value={draft.name}
                onChange={(name) => update({ name })}
                disabled={!canEdit}
                placeholder="Channel name"
              />
            )}
          </h1>
          {!isNew && <FieldProblem id="nc-name-problem">{check.errors.name}</FieldProblem>}
          <p className="mt-1 text-sm text-ink-500">
            {isNew
              ? "Where notifications and retry alerts go. Pick how it sends, then where to."
              : "Subscriptions and retry policies pick this channel to send through."}
            {isNew && returnTo && " Create takes you back with it picked, and nothing you had there is lost."}
          </p>
        </div>
        {existing.data && (
          <MoreActionsMenu
            actions={[
              {
                label: "Delete",
                icon: <Trash2 className="size-4" />,
                danger: true,
                onSelect: () => setDeleting(true),
                // Unavailable rather than name-checked: a channel still in use is not offered for
                // deleting at all, and the way out is pointing its users at another one.
                disabled: existing.data.usedBy.length > 0,
                title: existing.data.usedBy.length > 0 ? "Still in use - point its users at another channel first." : undefined,
                hidden: !canDelete,
              },
            ]}
          />
        )}
      </div>

      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="min-w-0 space-y-5">
          {isNew && (
            <div className="max-w-sm">
              <Field label="Channel name" htmlFor="nc-name" error={check.errors.name}>
                <TextInput
                  id="nc-name"
                  autoFocus
                  value={draft.name}
                  disabled={!canEdit}
                  placeholder="e.g. Ops email"
                  onChange={(e) => update({ name: e.target.value })}
                />
              </Field>
            </div>
          )}
          <Panel
            title="How it sends"
            description="Email, Teams and Slack get a message Bitween writes: what ran, how it ended, the error, and a link back."
          >
            <TypeCards
              selected={type}
              otherSelected={other}
              disabled={!canEdit}
              onPick={pickType}
              onPickOther={pickOther}
            />
            {!other && check.errors.type && (
              <div className="mt-3">
                <FieldProblem id="nc-type-problem">{check.errors.type}</FieldProblem>
              </div>
            )}
          </Panel>

          {type !== null && (
            <Panel
              title={type.adapterId === "NativeEmailMessageHandler" ? "Recipients" : "Where it posts"}
              description={
                type.adapterId === "NativeEmailMessageHandler"
                  ? undefined
                  : "Global values and partner properties can stand in for the address with the { } button."
              }
            >
              {check.errors.fields && (
                <div className="mb-3">
                  <FieldProblem id="nc-fields-problem">{check.errors.fields}</FieldProblem>
                </div>
              )}
              {type.adapterId === "NativeEmailMessageHandler" && <MailServerNote />}
              <div className="space-y-4">
                {TYPE_FIELDS[type.adapterId].map(({ prop, label }) => (
                  <PropField
                    key={prop.key}
                    prop={prop}
                    label={label}
                    value={draft.handlerProperties[prop.key] ?? ""}
                    disabled={!canEdit}
                    source={null}
                    onChange={(value) => {
                      const handlerProperties = { ...draft.handlerProperties };
                      if (value === "") delete handlerProperties[prop.key];
                      else handlerProperties[prop.key] = value;
                      update({ handlerProperties });
                    }}
                  />
                ))}
              </div>
              {type.adapterId === "NativeEmailMessageHandler" && (
                <p className="mt-3 text-[13px] text-ink-500">
                  A partner property such as <code className="font-mono text-[12px]">{"{{partner.CONTACT}}"}</code>{" "}
                  sends each exchange to its own partner's address.
                </p>
              )}
            </Panel>
          )}

          {other && (
            <Panel
              title="Adapter"
              description="Any handler adapter, built in or custom. It is handed the notification as JSON, as a legacy notifier was."
            >
              {(check.errors.adapter ?? check.errors.type) && (
                <div className="mb-3">
                  <FieldProblem id="nc-adapter-problem">{check.errors.adapter ?? check.errors.type}</FieldProblem>
                </div>
              )}
              <AdapterConfig
                kind="handler"
                adapterId={draft.handlerId}
                properties={draft.handlerProperties}
                onChange={(handlerId, handlerProperties) => update({ handlerId, handlerProperties })}
                disabled={!canEdit}
                required
              />
            </Panel>
          )}
        </div>

        <div className="min-w-0 space-y-5">
          {existing.data && <TestPanel channelId={existing.data.id} pending={dirty} canTest={canEdit} />}
          {existing.data && <UsedByPanel channel={existing.data} />}
          {isNew && (
            <Panel title="Test it" description="Once it's created, a test message can be sent from here.">
              <p className="text-[13px] text-ink-500">Nothing is sent until then.</p>
            </Panel>
          )}
        </div>
      </div>

      {existing.data && (
        <div className="mt-5 space-y-5">
          <SendsPanel channelId={existing.data.id} />
          <HistoryCard entityName="NotificationChannel" entityKey={existing.data.id} />
        </div>
      )}

      {isNew ? (
        <>
          <div className="mt-5 flex flex-wrap items-center justify-end gap-3 rounded-xl border border-ink-200 bg-white px-4 py-3">
            <StillNeeds missing={missing} />
            <Button onClick={() => leave(() => navigate(back, { replace: true }))}>Cancel</Button>
            <Button
              variant="primary"
              busy={save.isPending}
              disabled={!canEdit}
              onClick={() => check.submit(() => save.mutate())}
            >
              Create channel
            </Button>
          </div>
          <FormError>{save.error?.message}</FormError>
        </>
      ) : (
        canEdit &&
        dirty && (
          <UnsavedBar
            busy={save.isPending}
            error={save.error?.message ?? (missing.length > 0 ? `Still needs ${missing.join(" and ")}.` : undefined)}
            onSave={() => check.submit(() => save.mutate())}
            onDiscard={() => {
              setOtherChosen(false);
              setDraft(null);
            }}
          />
        )
      )}

      {leaveDialog}
      {deleting && existing.data && (
        <ConfirmDialog
          title={`Delete ${existing.data.name}?`}
          body={<p>Nothing uses it, so nothing stops sending.</p>}
          confirmLabel="Delete channel"
          onConfirm={async () => {
            await api.deleteNotificationChannel(existing.data.id);
            void queryClient.invalidateQueries({ queryKey: keys.notificationChannels.all });
            leave(() => navigate(back, { replace: true }));
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}

/** Email, Teams and Slack up front; every other adapter one step away. */
function TypeCards({
  selected,
  otherSelected,
  disabled,
  onPick,
  onPickOther,
}: {
  selected: ChannelType | null;
  otherSelected: boolean;
  disabled: boolean;
  onPick: (type: ChannelType) => void;
  onPickOther: () => void;
}) {
  return (
    <div>
      <div role="radiogroup" aria-label="How it sends" className="grid gap-3 sm:grid-cols-3">
        {CHANNEL_TYPES.map((t) => {
          const on = selected?.adapterId === t.adapterId;
          const Icon = t.icon;
          return (
            <button
              key={t.adapterId}
              type="button"
              role="radio"
              aria-checked={on}
              disabled={disabled}
              onClick={() => onPick(t)}
              className={`flex flex-col items-start gap-1.5 rounded-xl border px-4 py-3 text-left transition-colors disabled:cursor-not-allowed ${
                on
                  ? "border-crimson-400 bg-crimson-50 ring-2 ring-crimson-100"
                  : "border-ink-200 bg-white hover:border-ink-300 hover:bg-ink-50 disabled:hover:bg-white"
              }`}
            >
              <span className="flex items-center gap-2 text-[15px] font-semibold text-ink-900">
                <Icon className={`size-4.5 ${on ? "text-crimson-600" : "text-ink-500"}`} aria-hidden />
                {t.label}
              </span>
              <span className="text-[12px] leading-snug text-ink-500">{t.needs}</span>
            </button>
          );
        })}
      </div>
      <button
        type="button"
        role="radio"
        aria-checked={otherSelected}
        disabled={disabled}
        onClick={onPickOther}
        title="An HTTP endpoint, your own SMTP template, or a custom adapter deployed to this instance."
        className={`mt-3 inline-flex items-center gap-1.5 rounded-md px-1.5 py-1 text-[13px] font-medium disabled:cursor-not-allowed ${
          otherSelected ? "bg-ink-900 text-white" : "text-ink-600 hover:bg-ink-50 hover:text-ink-900"
        }`}
      >
        <Plug className="size-3.5" aria-hidden />
        Use another adapter (HTTP, own SMTP template, custom)
        {!otherSelected && <ArrowRight className="size-3.5" aria-hidden />}
      </button>
    </div>
  );
}

/**
 * Which server an Email channel sends through. Read from Settings by whoever may see them; the
 * rest are only told where it is set.
 */
function MailServerNote() {
  const canViewSettings = useSessionCan("settings.view");
  const settings = useQuery({
    queryKey: keys.settings.list,
    queryFn: () => api.listSettings(),
    enabled: canViewSettings,
  });
  const link = `/settings?section=${encodeURIComponent("Mail server")}`;

  if (!canViewSettings || !settings.data)
    return <p className="mb-3 text-[13px] text-ink-500">Sends through the mail server set in Settings.</p>;

  const value = (key: string) => settings.data.find((s) => s.key === key)?.value ?? "";
  const host = value("Bitween.MailServer.Host");
  const from = value("Bitween.MailServer.From");

  if (!host || !from)
    return (
      <p className="mb-3 rounded-lg bg-warn-100 px-3 py-2 text-[13px] text-warn-700">
        No mail server is set up yet, so Email channels can't send.{" "}
        <Link to={link} className="font-medium underline">
          Set one in Settings
        </Link>
        .
      </p>
    );

  return (
    <p className="mb-3 text-[13px] text-ink-500">
      Sends through <span className="font-medium text-ink-700">{host}</span> as{" "}
      <span className="font-medium text-ink-700">{from}</span>.{" "}
      <Link to={link} className="font-medium link">
        Change in Settings
      </Link>
    </p>
  );
}

function TestPanel({ channelId, pending, canTest }: { channelId: number; pending: boolean; canTest: boolean }) {
  const test = useMutation({ mutationFn: () => api.testNotificationChannel(channelId) });

  return (
    <Panel title="Test it" description="Sends a test message through the channel as it is saved.">
      <Button
        size="sm"
        busy={test.isPending}
        disabled={pending || !canTest}
        title={pending ? "Save your changes first - the test uses the saved channel." : undefined}
        onClick={() => test.mutate()}
      >
        <Send className="size-3.5" aria-hidden /> Send a test message
      </Button>
      {pending && <p className="mt-2 text-[13px] text-ink-500">Save your changes first - the test uses the saved channel.</p>}
      {test.data?.sent && (
        <p role="status" className="mt-2 text-[13px] text-ok-800">
          Sent. Check that it arrived.
        </p>
      )}
      {test.data && !test.data.sent && (
        <p role="alert" className="mt-2 break-words">
          <ProblemText>Not sent: {test.data.error ?? "the adapter gave no reason."}</ProblemText>
        </p>
      )}
      <FormError>{test.error?.message}</FormError>
    </Panel>
  );
}

function UsedByPanel({ channel }: { channel: NotificationChannelDetail }) {
  return (
    <Panel title="Used by" description="Every place that sends through this channel.">
      {channel.usedBy.length === 0 ? (
        <p className="text-[13px] text-ink-500">Nothing yet. Pick it on a subscription or a retry policy.</p>
      ) : (
        <ul className="space-y-1">
          {channel.usedBy.map((use, i) => {
            const href = channelUseHref(use);
            const text = describeChannelUse(use);
            return (
              <li key={i} className="text-[13px] text-ink-700">
                {href ? (
                  <Link to={href} className="hover:link">
                    {text}
                  </Link>
                ) : (
                  text
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

const SENDS_PAGE = 20;

/** Every delivery attempt through the channel, newest first, a page at a time. */
function SendsPanel({ channelId }: { channelId: number }) {
  const [failedOnly, setFailedOnly] = useState(false);
  const [page, setPage] = useState(0);

  const sends = useQuery({
    queryKey: keys.notificationChannels.sends(channelId, failedOnly, page),
    queryFn: () =>
      api.listNotificationChannelSends(channelId, { failedOnly, offset: page * SENDS_PAGE, limit: SENDS_PAGE }),
    placeholderData: (previous) => previous,
  });

  const total = sends.data?.total ?? 0;
  const pages = Math.max(1, Math.ceil(total / SENDS_PAGE));

  return (
    <Panel title="Recent sends" description="Every send through this channel, held back ones included, and why any failed.">
      <div role="tablist" aria-label="Filter sends" className="mb-2 flex flex-wrap gap-1.5">
        {[
          { key: false, label: "All" },
          { key: true, label: "Failed" },
        ].map((f) => {
          const on = f.key === failedOnly;
          return (
            <button
              key={f.label}
              role="tab"
              aria-selected={on}
              onClick={() => {
                setFailedOnly(f.key);
                setPage(0);
              }}
              className={`rounded-lg px-2.5 py-1 text-[13px] font-medium transition-colors ${
                on ? "bg-ink-900 text-white" : "bg-ink-50 text-ink-600 hover:bg-ink-100 hover:text-ink-900"
              }`}
            >
              {f.label}
            </button>
          );
        })}
      </div>

      {sends.isPending ? (
        <LoadingBlock label="Loading sends…" />
      ) : (
        <MiniTable
          rows={sends.data?.result ?? []}
          rowKey={(n) => n.id}
          pageSize={SENDS_PAGE}
          empty={failedOnly ? "Nothing has failed to send." : "Nothing sent yet."}
          columns={[
            {
              header: "When",
              className: "whitespace-nowrap",
              cell: (n) => (
                <span className="text-xs text-ink-500" title={formatDateTime(n.on)}>
                  {timeAgo(n.on)}
                </span>
              ),
            },
            {
              header: "About",
              headerTitle: "The subscription whose exchange it reported, and whether it was a retry or auto-pause alert.",
              truncate: true,
              cell: (n) => (
                <span className="flex min-w-0 items-center gap-1.5">
                  {n.subscriptionId !== null ? (
                    <Link
                      to={`/subscriptions/${n.subscriptionId}`}
                      className="truncate font-medium text-ink-800 hover:text-crimson-700"
                    >
                      {n.subscriptionName}
                    </Link>
                  ) : (
                    <span className="text-ink-500 italic">exchange removed</span>
                  )}
                  {n.sentFor === "Retry budget alert" && (
                    <Badge tone="warn" title="A retry group ran out of its budget.">
                      Retry alert
                    </Badge>
                  )}
                  {n.sentFor === "Auto-pause alert" && (
                    <Badge tone="warn" title="The subscription paused itself after failures in a row.">
                      Auto-pause
                    </Badge>
                  )}
                </span>
              ),
            },
            {
              header: "Exchange",
              cell: (n) => (
                <Link
                  to={`/exchanges?ids=${encodeURIComponent(n.xchangeId)}`}
                  className="font-mono text-xs text-ink-600 hover:link"
                >
                  {n.xchangeId}
                </Link>
              ),
            },
            {
              header: "Result",
              cell: (n) =>
                n.success ? (
                  <Badge tone="ok">Sent</Badge>
                ) : n.heldBack ? (
                  <Badge
                    tone="neutral"
                    title="Another message like it went out minutes before, so this one was not sent. The next message says how many were held back."
                  >
                    Held back
                  </Badge>
                ) : (
                  <Badge tone="danger">Failed</Badge>
                ),
            },
            {
              header: "Reason",
              truncate: true,
              cell: (n) =>
                n.exception ? (
                  <ExceptionLine text={n.exception} icon className="font-mono text-[11px] text-danger-700" />
                ) : (
                  <span className="text-ink-500">-</span>
                ),
            },
          ]}
        />
      )}

      {total > SENDS_PAGE && (
        <div className="mt-2 flex items-center justify-between gap-2 text-[12px] text-ink-500">
          <span className="tabular-nums">
            {page * SENDS_PAGE + 1}-{Math.min(total, (page + 1) * SENDS_PAGE)} of {total}
          </span>
          <span className="flex gap-1.5">
            <Button size="sm" disabled={page === 0} onClick={() => setPage((p) => p - 1)}>
              Newer
            </Button>
            <Button size="sm" disabled={page >= pages - 1} onClick={() => setPage((p) => p + 1)}>
              Older
            </Button>
          </span>
        </div>
      )}
    </Panel>
  );
}
