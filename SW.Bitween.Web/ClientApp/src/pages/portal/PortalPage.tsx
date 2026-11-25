import { useState } from "react";
import { Link } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Download, ExternalLink, Inbox, LogOut } from "lucide-react";
import { api, type PortalExchange, type PortalExchangeStatus } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSession } from "../../auth/useSession";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { ExceptionBlock } from "../../components/ui/Exception";
import { Select } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { SegmentedControl } from "../../components/ui/SegmentedControl";
import { duration, formatDateTime, timeAgo } from "../../lib/dates";
import { bytes, whole } from "../../lib/numbers";
import { XchangeId } from "../exchanges/shared";

const PAGE_SIZE = 25;

const STATUS: Record<PortalExchangeStatus, { label: string; tone: "ok" | "danger" | "neutral"; title: string }> = {
  success: { label: "Delivered", tone: "ok", title: "Finished without an error." },
  failed: { label: "Failed", tone: "danger", title: "Finished with an error. Open it to see why, if your role shows errors." },
  running: { label: "Running", tone: "neutral", title: "Started and not finished yet." },
};

const FILE_LABELS: Record<string, { label: string; title: string }> = {
  input: { label: "Received", title: "What came in, before any mapping." },
  output: { label: "Sent", title: "What was delivered, after mapping." },
  response: { label: "Response", title: "What the receiving system answered." },
};

/**
 * The partner portal: the one page a partner's person sees. Their subscriptions and exchanges only;
 * the server scopes every call to their partner, so nothing here has to filter.
 */
export function PortalPage() {
  const { session, signOut } = useSession();
  const canExchanges = useSessionCan("portal-exchanges.view");
  const canSubscriptions = useSessionCan("portal-subscriptions.view");

  return (
    <div className="min-h-screen bg-canvas">
      <header className="flex items-center gap-3 border-b border-ink-200 bg-white px-4 py-2.5 sm:px-6">
        <img src={import.meta.env.BASE_URL + "brand/BitweenIcon.svg"} alt="" className="size-6" />
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-[15px] font-semibold text-ink-900">{session?.partner?.name}</h1>
          <p className="text-xs text-ink-500" title="Everything here belongs to your company, and nobody else's.">
            Partner portal
          </p>
        </div>
        <span className="hidden text-sm text-ink-600 sm:inline">{session?.user.displayName}</span>
        <Link to="/change-password" className="text-sm text-ink-600 underline-offset-2 hover:underline" title="Choose a new password">
          Password
        </Link>
        <Button size="sm" variant="ghost" onClick={() => void signOut()} title="Sign out of the partner portal">
          <LogOut className="size-4" /> Sign out
        </Button>
      </header>

      <main className="mx-auto max-w-7xl space-y-4 px-4 py-4 sm:px-6">
        {!canExchanges && !canSubscriptions ? (
          <EmptyState icon={<Inbox />} title="Nothing to show yet">
            Your role doesn't include anything to see here. Ask whoever set up your account to give you a role.
          </EmptyState>
        ) : (
          <>
            {canExchanges && <Summary />}
            <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_20rem]">
              {canExchanges ? <Exchanges /> : <div />}
              {canSubscriptions && <Subscriptions />}
            </div>
          </>
        )}
      </main>
    </div>
  );
}

function Summary() {
  const summary = useQuery({ queryKey: keys.portal.summary, queryFn: () => api.getPortalSummary(), refetchInterval: 60_000 });
  if (summary.isError) return <LoadError error={summary.error} what="your last 24 hours" onRetry={() => void summary.refetch()} />;
  const tiles = [
    { label: "Delivered", value: summary.data?.succeeded, title: "Exchanges in the last 24 hours that finished without an error." },
    { label: "Failed", value: summary.data?.failed, title: "Exchanges in the last 24 hours that finished with an error." },
    { label: "Running", value: summary.data?.running, title: "Exchanges in the last 24 hours that haven't finished yet." },
  ];
  return (
    <section aria-label="Last 24 hours" className="grid grid-cols-3 gap-3">
      {tiles.map((t) => (
        <div key={t.label} className="rounded-xl border border-ink-200 bg-white px-4 py-3" title={t.title}>
          <div className="text-xs text-ink-500">{t.label} · 24 h</div>
          <div className="mt-0.5 text-xl font-semibold tabular-nums text-ink-900">{t.value == null ? "-" : whole(t.value)}</div>
        </div>
      ))}
    </section>
  );
}

function Subscriptions() {
  const subscriptions = useQuery({ queryKey: keys.portal.subscriptions, queryFn: () => api.listPortalSubscriptions() });
  return (
    <Panel title="Subscriptions" description="What runs for your company.">
      {subscriptions.isPending ? (
        <LoadingBlock />
      ) : subscriptions.isError ? (
        <LoadError error={subscriptions.error} what="your subscriptions" onRetry={() => void subscriptions.refetch()} />
      ) : subscriptions.data.length === 0 ? (
        <p className="text-sm text-ink-500">None yet.</p>
      ) : (
        <ul className="divide-y divide-ink-100">
          {subscriptions.data.map((s) => (
            <li key={s.id} className="flex items-center gap-2 py-2">
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm font-medium text-ink-900" title={s.name}>
                  {s.name}
                </div>
                <div className="truncate text-xs text-ink-500" title="The kind of information it carries">
                  {s.documentName}
                </div>
              </div>
              {s.paused && (
                <Badge tone="warn" title="Paused: nothing runs through it until it is resumed.">
                  Paused
                </Badge>
              )}
            </li>
          ))}
        </ul>
      )}
    </Panel>
  );
}

function Exchanges() {
  const canSubscriptions = useSessionCan("portal-subscriptions.view");
  const [status, setStatus] = useState<PortalExchangeStatus | "all">("all");
  const [subscriptionId, setSubscriptionId] = useState("");
  const [page, setPage] = useState(0);
  // Kept as the row opened, and followed as the list refreshes: an exchange opened while running
  // shows its result once it has one. Off the page after a refresh, the last copy stays open.
  const [opened, setOpen] = useState<PortalExchange | null>(null);

  const query = { status: status === "all" ? null : status, subscriptionId: subscriptionId || null, page, size: PAGE_SIZE };
  const exchanges = useQuery({
    queryKey: keys.portal.exchanges(query),
    queryFn: () => api.searchPortalExchanges(query),
    placeholderData: keepPreviousData,
    refetchInterval: page === 0 ? 30_000 : false,
  });
  const subscriptions = useQuery({
    queryKey: keys.portal.subscriptions,
    queryFn: () => api.listPortalSubscriptions(),
    enabled: canSubscriptions,
  });

  return (
    <Panel
      title="Exchanges"
      description="Every message that ran for your company, newest first."
      action={
        <div className="flex flex-wrap items-center gap-2">
          <SegmentedControl
            label="Show exchanges that are"
            size="sm"
            value={status}
            onChange={(v) => {
              setStatus(v);
              setPage(0);
            }}
            options={[
              { value: "all", label: "All", title: "Every exchange" },
              { value: "failed", label: "Failed", title: STATUS.failed.title },
              { value: "running", label: "Running", title: STATUS.running.title },
              { value: "success", label: "Delivered", title: STATUS.success.title },
            ]}
          />
          {/* Still shown while a filter is on, even if the list has shrunk, so it can be cleared. */}
          {canSubscriptions && ((subscriptions.data?.length ?? 0) > 1 || subscriptionId !== "") && (
            <div className="w-52">
              <Select
                aria-label="Subscription"
                title="Show one subscription's exchanges"
                className="h-8"
              value={subscriptionId}
              onChange={(e) => {
                setSubscriptionId(e.target.value);
                setPage(0);
              }}
                options={[{ value: "", label: "All subscriptions" }, ...(subscriptions.data ?? []).map((s) => ({ value: s.id, label: s.name }))]}
              />
            </div>
          )}
        </div>
      }
    >
      {exchanges.isPending ? (
        <LoadingBlock />
      ) : exchanges.isError ? (
        <LoadError error={exchanges.error} what="your exchanges" onRetry={() => void exchanges.refetch()} />
      ) : exchanges.data.result.length === 0 ? (
        <p className="py-6 text-center text-sm text-ink-500">{page === 0 ? "No exchanges match." : "No more exchanges."}</p>
      ) : (
        <div className="-mx-4 overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-y border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-1.5 font-medium" title="When it started">Started</th>
                <th className="px-2 py-1.5 font-medium" title="How it ended, or that it is still running">Status</th>
                <th className="px-2 py-1.5 font-medium" title="The subscription it ran through">Subscription</th>
                <th className="hidden px-2 py-1.5 font-medium sm:table-cell" title="The kind of information it carried">Information type</th>
                <th className="hidden px-4 py-1.5 text-right font-medium sm:table-cell" title="From start to finish">Took</th>
              </tr>
            </thead>
            <tbody>
              {exchanges.data.result.map((x) => (
                <tr
                  key={x.id}
                  onClick={() => setOpen(x)}
                  className="cursor-pointer border-b border-ink-100 hover:bg-ink-50 focus-within:bg-ink-50"
                >
                  <td className="px-4 py-1.5 whitespace-nowrap text-ink-700" title={formatDateTime(x.startedOn)}>
                    {/* The row is a shortcut for the mouse; this is the control, for everyone. */}
                    <button
                      type="button"
                      className="text-left focus:outline-none"
                      aria-label={`Open the exchange of ${formatDateTime(x.startedOn)}`}
                      onClick={(e) => {
                        e.stopPropagation();
                        setOpen(x);
                      }}
                    >
                      {timeAgo(x.startedOn)}
                    </button>
                  </td>
                  <td className="px-2 py-1.5">
                    <Badge tone={STATUS[x.status].tone} title={STATUS[x.status].title}>
                      {STATUS[x.status].label}
                    </Badge>
                  </td>
                  <td className="max-w-56 truncate px-2 py-1.5 text-ink-900">{x.subscriptionName ?? "-"}</td>
                  <td className="hidden max-w-48 truncate px-2 py-1.5 text-ink-600 sm:table-cell">{x.documentName}</td>
                  <td className="hidden px-4 py-1.5 text-right whitespace-nowrap text-ink-600 tabular-nums sm:table-cell">
                    {x.finishedOn ? duration(x.startedOn, x.finishedOn) : "-"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {exchanges.data && (page > 0 || exchanges.data.hasMore) && (
        <div className="mt-3 flex items-center justify-end gap-2">
          {/* Off while the next page loads: the rows and "more" shown are still the last page's. */}
          <Button size="sm" disabled={page === 0 || exchanges.isPlaceholderData} onClick={() => setPage((p) => p - 1)} title="Back to newer exchanges">
            Newer
          </Button>
          <span className="text-xs text-ink-500 tabular-nums">Page {page + 1}</span>
          <Button
            size="sm"
            disabled={!exchanges.data.hasMore || exchanges.isPlaceholderData}
            onClick={() => setPage((p) => p + 1)}
            title="On to older exchanges"
          >
            Older
          </Button>
        </div>
      )}
      {opened && (
        <ExchangeDialog x={exchanges.data?.result.find((x) => x.id === opened.id) ?? opened} onClose={() => setOpen(null)} />
      )}
    </Panel>
  );
}

function ExchangeDialog({ x, onClose }: { x: PortalExchange; onClose: () => void }) {
  return (
    <Dialog title="Exchange" onClose={onClose}>
      <dl className="grid grid-cols-[8rem_minmax(0,1fr)] gap-x-3 gap-y-1.5 text-sm">
        <dt className="text-ink-500">Id</dt>
        <dd>
          <XchangeId id={x.id} />
        </dd>
        <dt className="text-ink-500">Status</dt>
        <dd>
          <Badge tone={STATUS[x.status].tone} title={STATUS[x.status].title}>
            {STATUS[x.status].label}
          </Badge>
        </dd>
        <dt className="text-ink-500">Subscription</dt>
        <dd className="text-ink-900">{x.subscriptionName ?? "-"}</dd>
        <dt className="text-ink-500">Information type</dt>
        <dd className="text-ink-900">{x.documentName}</dd>
        <dt className="text-ink-500">Started</dt>
        <dd className="text-ink-900">{formatDateTime(x.startedOn)}</dd>
        <dt className="text-ink-500">Finished</dt>
        <dd className="text-ink-900">
          {x.finishedOn ? `${formatDateTime(x.finishedOn)} (took ${duration(x.startedOn, x.finishedOn)})` : "Not yet"}
        </dd>
      </dl>

      {x.error && (
        <div className="mt-4">
          <ExceptionBlock text={x.error} title="Why it failed" />
        </div>
      )}

      {x.files && (
        <div className="mt-4">
          <h3 className="mb-1.5 text-sm font-semibold text-ink-900">Files</h3>
          {x.files.length === 0 ? (
            <p className="text-sm text-ink-500">This exchange has no files.</p>
          ) : (
            <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
              {x.files.map((f) => (
                <li key={f.kind} className="flex items-center gap-2 px-3 py-2 text-sm">
                  <span className="w-20 shrink-0 font-medium text-ink-900" title={FILE_LABELS[f.kind]?.title}>
                    {FILE_LABELS[f.kind]?.label ?? f.kind}
                  </span>
                  <span className="min-w-0 flex-1 truncate text-ink-500" title={f.name ?? undefined}>
                    {f.name ?? "Unnamed"} · {bytes(f.size)}
                  </span>
                  <a href={f.url} target="_blank" rel="noreferrer" className="text-ink-600 hover:text-ink-900" title="Open in a new tab">
                    <ExternalLink className="size-4" />
                    <span className="sr-only">Open {FILE_LABELS[f.kind]?.label ?? f.kind}</span>
                  </a>
                  <a href={f.url} download={f.name ?? `${x.id}-${f.kind}`} className="text-ink-600 hover:text-ink-900" title="Download">
                    <Download className="size-4" />
                    <span className="sr-only">Download {FILE_LABELS[f.kind]?.label ?? f.kind}</span>
                  </a>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Dialog>
  );
}
