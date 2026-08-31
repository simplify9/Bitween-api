import { useState, type FormEvent } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, RotateCcw, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Button, EmptyState, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { Pagination } from "../../components/ui/Pagination";
import { Table } from "../../components/ui/Table";
import { UsedByCell, useSubscriptionsCache } from "../../components/config/shared";
import { keys } from "../../api/queryKeys";

function CreateRetryPolicyDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");

  const create = useMutation({
    mutationFn: () => api.createRetryPolicy({ name }),
    onSuccess: (policy) => {
      void queryClient.invalidateQueries({ queryKey: keys.retryPolicies.all });
      navigate(`/retry-policies/${policy.id}`);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <Dialog title="New retry policy" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Name" htmlFor="nrp-name" hint="Groups and budgets are added on the policy's page.">
          <TextInput
            id="nrp-name"
            required
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Transient failures"
          />
        </Field>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create policy
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

const PAGE_SIZE = 25;

export function RetryPoliciesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
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

  const setParam = (key: string, value: string | null, resetOffset = key === "q") =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        if (resetOffset) next.delete("offset");
        return next;
      },
      { replace: key === "q" },
    );

  const rows = policies.data?.result ?? [];
  const total = policies.data?.total ?? 0;

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
        actions={
          <Can permission="retry-policies.create">
            <Button variant="primary" onClick={() => setParam("new", "1")}>
              <Plus className="size-4" /> New retry policy
            </Button>
          </Can>
        }
      />

      <div className="relative mb-4 max-w-xs">
        <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-ink-400" />
        <input
          type="search"
          value={q}
          onChange={(e) => setParam("q", e.target.value || null)}
          placeholder="Search retry policies"
          aria-label="Search retry policies"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {policies.isPending ? (
        <LoadingBlock label="Loading retry policies…" />
      ) : rows.length === 0 ? (
        <EmptyState icon={<RotateCcw />} title={q ? "No policies match" : "No retry policies yet"}>
          {q ? "Try a different search." : "Create a policy to control what happens after failures."}
        </EmptyState>
      ) : (
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
              onOffsetChange={(o) => setParam("offset", String(o), false)}
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

      {creating && <CreateRetryPolicyDialog onClose={() => setParam("new", null)} />}
    </div>
  );
}
