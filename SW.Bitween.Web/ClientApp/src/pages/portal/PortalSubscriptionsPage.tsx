import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, EmptyState, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Table } from "../../components/ui/Table";

/** What runs for their company, read only. */
export function PortalSubscriptionsPage() {
  const subscriptions = useQuery({ queryKey: keys.portal.subscriptions, queryFn: () => api.listPortalSubscriptions() });
  const paused = (p: boolean) =>
    p ? (
      <Badge tone="warn" title="Paused: nothing runs through it until it is resumed.">
        Paused
      </Badge>
    ) : (
      <Badge tone="ok" title="Running: what comes in for it is processed.">
        Running
      </Badge>
    );

  return (
    <div>
      <PageHeader title="Subscriptions" description="What runs for your company." />
      {subscriptions.isPending ? (
        <LoadingBlock />
      ) : subscriptions.isError ? (
        <LoadError error={subscriptions.error} what="your subscriptions" onRetry={() => void subscriptions.refetch()} />
      ) : subscriptions.data.length === 0 ? (
        <EmptyState title="None yet">When something runs for your company, it shows here.</EmptyState>
      ) : (
        <Table
          rows={subscriptions.data}
          rowKey={(s) => s.id}
          columns={[
            { header: "Name", wrap: true, cell: (s) => <span className="font-medium text-ink-900">{s.name}</span> },
            { header: "Information type", headerTitle: "The kind of information it carries", cell: (s) => s.documentName },
            { header: "State", headerTitle: "Running, or paused", align: "right", cell: (s) => paused(s.paused) },
          ]}
          card={(s) => ({ title: s.name, status: paused(s.paused), subtitle: s.documentName })}
        />
      )}
    </div>
  );
}
