import { Link, useNavigate } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { BellRing } from "lucide-react";
import { api } from "../../api";
import { useAdapterCatalog } from "../../components/config/adapterCatalog";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { keys } from "../../api/queryKeys";
import { UsedByCell } from "../../components/config/shared";
import { useSubscriptionsCache } from "../../components/config/lookups";
import { useListParams } from "../../lib/listParams";
import { SearchBox } from "../../components/ui/SearchBox";

const PAGE_SIZE = 25;

export function NotifiersPage() {
  const { params: searchParams, set: setParam, searchText, setSearchText } = useListParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;

  const notifiers = useQuery({
    queryKey: keys.notifiers.search({ q, offset }),
    queryFn: () => api.searchNotifiers({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const channels = useAdapterCatalog("handler");
  // Notifiers carry subscription ids only, so the names come from the shared cache
  // every other screen already reads.
  const subscriptions = useSubscriptionsCache();


  const filtered = notifiers.data?.result ?? [];
  const total = notifiers.data?.total ?? 0;

  const channelLabel = (id: string) => channels.data?.find((c) => c.id === id)?.label ?? id;

  return (
    <div>
      <PageHeader
        title="Legacy notifiers"
        description="Notifiers made before notification channels. They still run and can be edited, but no new ones can be made."
        help={{
          title: "How notifiers work",
          body: (
            <>
              <p>
                Notifications are now set on each subscription: pick a <strong>notification channel</strong> and
                the outcomes to send on. Channels are set up once in Settings and shared by every subscription
                and retry policy that uses them.
              </p>
              <p>
                A legacy notifier <strong>watches</strong> a set of subscriptions and still sends through its
                own adapter when one of them finishes with an outcome it cares about.
              </p>
              <p>
                A notifier that watches no subscriptions never sends anything. Every delivery
                attempt is recorded on the notifier's page.
              </p>
            </>
          ),
        }}
        actions={
          <Link
            to={`/settings?section=${encodeURIComponent("Notification channels")}`}
            className="text-[13px] font-medium text-crimson-700 hover:underline"
          >
            Notification channels
          </Link>
        }
      />

      <SearchBox value={searchText} onChange={setSearchText} label="Search notifiers" className="mb-4 max-w-xs" />

      {notifiers.isPending ? (
        <LoadingBlock label="Loading notifiers…" />
      ) : notifiers.isError ? (
        <LoadError error={notifiers.error} what="notifiers" onRetry={() => void notifiers.refetch()} />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<BellRing />} title={q ? "No notifiers match" : "No legacy notifiers"}>
          {q ? "Try a different search." : "Set notifications on a subscription instead, through a notification channel."}
        </EmptyState>
      ) : (
        <Table
          rows={filtered}
          rowKey={(n) => n.id}
          onRowClick={(n) => navigate(`/notifiers/${n.id}`)}
          footer={
            <Pagination
              offset={offset}
              limit={PAGE_SIZE}
              total={total}
              onOffsetChange={(o) => setParam("offset", String(o))}
            />
          }
          columns={[
            { header: "Notifier", cell: (n) => <span className="font-medium text-ink-900">{n.name}</span> },
            {
              header: "Sends when",
              cell: (n) => (
                <span
                  className="flex flex-wrap gap-1"
                  title="Any combination of outcomes can be set - these are all the ones this notifier sends on, not a single state."
                >
                  {n.onFailed && <Badge tone="danger">Failed</Badge>}
                  {n.onBadResult && <Badge tone="warn">Bad result</Badge>}
                  {n.onSuccess && <Badge tone="ok">Success</Badge>}
                  {!n.onFailed && !n.onBadResult && !n.onSuccess && <span className="text-ink-500">Never</span>}
                </span>
              ),
            },
            {
              // Not "Channel": that word now means a notification channel, which a legacy notifier isn't.
              header: "Adapter",
              headerTitle: "The handler adapter this notifier sends through.",
              cell: (n) => <span className="text-ink-600">{channelLabel(n.channelId)}</span>,
            },
            {
              // Named, not counted: "1 subscription" tells you a notifier is wired up but
              // not to what, which is the only thing worth knowing from the list.
              header: "Watches",
              wrap: true,
              cell: (n) =>
                n.subscriptionIds.length === 0 ? (
                  <span className="text-warn-700">Nothing - never fires</span>
                ) : (
                  <UsedByCell
                    items={(subscriptions.data ?? []).filter((s) => n.subscriptionIds.includes(s.id))}
                  />
                ),
            },
            { header: "Status", cell: (n) => (n.enabled ? <Badge tone="ok">Active</Badge> : <Badge>Off</Badge>) },
          ]}
        />
      )}

    </div>
  );
}
