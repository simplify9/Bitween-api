import { useNavigate } from "react-router";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Plus, RotateCcw } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button } from "../../components/ui/basics";
import { ListBody } from "../../components/ui/ListBody";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { UsedByCell } from "../../components/config/shared";
import { useSubscriptionsCache } from "../../components/config/lookups";
import { CreateRetryPolicyDialog } from "../../components/config/RetryPolicyDialog";
import { keys } from "../../api/queryKeys";
import { useListParams } from "../../lib/listParams";
import { SearchBox } from "../../components/ui/SearchBox";

const PAGE_SIZE = 25;

export function RetryPoliciesPage() {
  const { params: searchParams, set: setParam, searchText, setSearchText } = useListParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const creating = searchParams.get("new") === "1";
  const offset = searchParams.get("offset") ? Number(searchParams.get("offset")) : 0;

  const policies = useQuery({
    queryKey: keys.retryPolicies.search({ q, offset }),
    queryFn: () => api.searchRetryPolicies({ search: q, offset, limit: PAGE_SIZE }),
    placeholderData: keepPreviousData,
  });
  const subscriptions = useSubscriptionsCache().data ?? [];


  const rows = policies.data?.result ?? [];
  const total = policies.data?.total ?? 0;

  // In the header, and in the empty list where there is nothing else to do.
  const createAction = (
    <Can permission="retry-policies.create">
      <Button variant="primary" onClick={() => setParam("new", "1")}>
        <Plus className="size-4" /> New retry policy
      </Button>
    </Can>
  );

  return (
    <div>
      <PageHeader
        title="Retry policies"
        description="Rules for what happens after an exchange fails — retry with a delay, or stop and alert."
        help={{
          title: "How retry policies work",
          body: (
            <>
              <p>
                A policy is a list of <strong>groups</strong>, checked in priority order (lowest
                first). The first group whose conditions match the failure decides: retry within a
                budget, or block retries entirely. If nothing matches, the exchange isn't retried.
              </p>
              <p>
                Assign a policy to a subscription to activate it. You can dry-run any policy against
                a sample error right on its page.
              </p>
            </>
          ),
        }}
        actions={createAction}
      />

      <SearchBox value={searchText} onChange={setSearchText} label="Search retry policies" className="mb-4 max-w-xs" />

      <ListBody
        query={policies}
        rows={rows}
        what="retry policies"
        filtered={!!q}
        empty={{
          icon: <RotateCcw />,
          title: "No retry policies yet",
          body: "Create a policy to control what happens after failures.",
          action: createAction,
        }}
      >
        {(rows) => (
          <Table
            rows={rows}
            rowKey={(p) => p.id}
            minWidth="min-w-130"
            onRowClick={(p) => navigate(`/retry-policies/${p.id}`)}
            footer={
              <Pagination
                offset={offset}
                limit={PAGE_SIZE}
                total={total}
                onOffsetChange={(o) => setParam("offset", String(o))}
              />
            }
            columns={[
              { header: "Policy", cell: (p) => <span className="font-medium text-ink-900">{p.name}</span> },
              {
                header: "Groups",
                align: "right",
                cell: (p) => <span className="tabular-nums text-ink-600">{p.groupCount || "—"}</span>,
              },
              {
                header: "Used by",
                wrap: true,
                cell: (p) => <UsedByCell items={subscriptions.filter((s) => s.retryPolicyId === p.id)} />,
              },
            ]}
          />
        )}
      </ListBody>

      {creating && <CreateRetryPolicyDialog onClose={() => setParam("new", null)} />}
    </div>
  );
}
