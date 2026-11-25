import { useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, type PortalMappingPlace } from "../../api";
import { keys } from "../../api/queryKeys";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";
import { formatDateTime, timeAgo } from "../../lib/dates";

function MappingBadges({ m }: { m: PortalMappingPlace }) {
  return (
    <span className="inline-flex flex-wrap gap-1">
      {m.waiting && (
        <Badge tone="warn" title="A change of yours waits for our approval.">
          Waiting
        </Badge>
      )}
      {m.setAside ? (
        <Badge tone="warn" title="Yours reads an internal value our mapping no longer sends, so ours is used for now.">
          Set aside
        </Badge>
      ) : m.customized ? (
        <Badge tone="ok" title="Your own mapping is used for your exchanges.">
          Yours
        </Badge>
      ) : (
        <Badge title="You get our mapping, the same as everyone on it.">Ours</Badge>
      )}
    </span>
  );
}

/** Every place they're connected, and whether what they receive there is shaped their own way. */
export function PortalMappingsPage() {
  const navigate = useNavigate();
  const mappings = useQuery({ queryKey: keys.portal.mappings, queryFn: () => api.listPortalMappings() });

  return (
    <div>
      <PageHeader
        title="Mappings"
        description="How what you receive is shaped, wherever you're connected. Open one to see it, or make your own version."
      />
      {mappings.isPending ? (
        <LoadingBlock />
      ) : mappings.isError ? (
        <LoadError error={mappings.error} what="your mappings" onRetry={() => void mappings.refetch()} />
      ) : mappings.data.length === 0 ? (
        <EmptyState title="Nothing is connected to you yet">When something runs for your company, it shows here.</EmptyState>
      ) : (
        <Table
          rows={mappings.data}
          rowKey={(m) => m.subscriptionId}
          onRowClick={(m) => void navigate(`/portal/mappings/${m.subscriptionId}`)}
          columns={[
            { header: "Subscription", wrap: true, cell: (m) => <span className="font-medium text-ink-900">{m.subscriptionName}</span> },
            { header: "How you're connected", headerTitle: "Your own subscription, an API gateway, or a bus gateway route", cell: (m) => m.via },
            { header: "Information type", cell: (m) => m.documentName },
            { header: "Mapping", headerTitle: "Ours, or your own version", cell: (m) => <MappingBadges m={m} /> },
            {
              header: "Changed",
              headerTitle: "When your own version last changed",
              align: "right",
              cell: (m) => (m.modifiedOn ? <span title={formatDateTime(m.modifiedOn)}>{timeAgo(m.modifiedOn)}</span> : "-"),
            },
          ]}
          card={(m) => ({ title: m.subscriptionName, status: <MappingBadges m={m} />, subtitle: `${m.via} · ${m.documentName}` })}
        />
      )}
    </div>
  );
}
