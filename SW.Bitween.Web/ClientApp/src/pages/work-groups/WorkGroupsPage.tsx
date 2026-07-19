import { Fragment, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowUpRight, ChevronDown, ChevronRight, Layers, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { IntegrationMiniList, useIntegrationsCache } from "../../components/config/shared";
import { formatDate } from "../../lib/dates";
import { LiveQueueStats } from "./LiveQueueStats";

export function WorkGroupsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";
  const [open, setOpen] = useState<Set<number>>(new Set());

  const groups = useQuery({ queryKey: ["work-groups"], queryFn: () => api.listWorkGroups() });
  const integrations = useIntegrationsCache();

  const setParam = (key: string, value: string | null) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: true },
    );

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (groups.data ?? []).filter(
      (g) => !needle || g.name.toLowerCase().includes(needle) || g.busMessageName.toLowerCase().includes(needle),
    );
  }, [groups.data, q]);

  const toggleOpen = (id: number) =>
    setOpen((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <div>
      <PageHeader
        title="Work groups"
        description="Give a set of integrations their own queue, priority and prefetch, separate from the default lane."
        help={{
          title: "How work groups work",
          body: (
            <>
              <p>
                Every integration runs in the default (ungrouped) lane unless assigned to a work
                group. Groups get their own RabbitMQ queue — <strong>prefetch</strong> controls how
                many messages a consumer pulls at once, <strong>priority</strong> decides which
                group's queue is drained first when several are busy.
              </p>
              <p>Changes to a group's settings apply live — no restart needed.</p>
            </>
          ),
        }}
        actions={
          <Can permission="workgroups.create">
            <Button variant="primary" onClick={() => navigate("/work-groups/new")}>
              <Plus className="size-4" /> New work group
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
          placeholder="Search work groups"
          aria-label="Search work groups"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {groups.isPending ? (
        <LoadingBlock label="Loading work groups…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Layers />} title={q ? "No work groups match" : "No work groups yet"}>
          {q ? "Try a different search." : "Create one to give a set of integrations their own queue."}
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-150 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="w-10 px-3 py-2.5" />
                <th className="px-3 py-2.5 font-medium">Name</th>
                <th className="px-3 py-2.5 font-medium">Bus message name</th>
                <th className="px-3 py-2.5 font-medium">Consumers</th>
                <th className="px-3 py-2.5 font-medium">Used by</th>
                <th className="px-3 py-2.5 font-medium">Created</th>
                <th className="w-10 px-3 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {filtered.map((g) => {
                const expanded = open.has(g.id);
                const assigned = (integrations.data ?? []).filter((s) => s.workGroupId === g.id);
                return (
                  <Fragment key={g.id}>
                    <tr
                      onClick={() => toggleOpen(g.id)}
                      className="cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50"
                    >
                      <td className="px-3 py-3">
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleOpen(g.id);
                          }}
                          aria-expanded={expanded}
                          aria-label={`Details for ${g.name}`}
                          className="rounded-md p-1 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          {expanded ? <ChevronDown className="size-3.5" /> : <ChevronRight className="size-3.5" />}
                        </button>
                      </td>
                      <td className="px-3 py-3 font-medium text-ink-900">{g.name}</td>
                      <td className="px-3 py-3">
                        <code className="font-mono text-xs text-ink-600">{g.busMessageName}</code>
                      </td>
                      <td className="px-3 py-3">
                        {g.consumerCount > 0 ? (
                          <Badge tone="ok">{g.consumerCount}</Badge>
                        ) : (
                          <span className="text-ink-400">—</span>
                        )}
                      </td>
                      <td className="px-3 py-3 text-ink-600">
                        {g.usedByCount > 0 ? (
                          `${g.usedByCount} integration${g.usedByCount === 1 ? "" : "s"}`
                        ) : (
                          <span className="text-ink-400">—</span>
                        )}
                      </td>
                      <td className="px-3 py-3 whitespace-nowrap text-ink-500">{formatDate(g.createdOn)}</td>
                      <td className="px-3 py-3">
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            navigate(`/work-groups/${g.id}`);
                          }}
                          aria-label={`Open ${g.name}`}
                          title="Open"
                          className="rounded-md p-1.5 text-ink-400 hover:bg-ink-100 hover:text-ink-700"
                        >
                          <ArrowUpRight className="size-4" />
                        </button>
                      </td>
                    </tr>
                    {expanded && (
                      <tr className="border-b border-ink-100 last:border-b-0">
                        <td />
                        <td colSpan={6} className="px-3 pt-0.5 pb-3">
                          <div className="space-y-3 rounded-lg bg-ink-50 px-3.5 py-3">
                            <Can permission="monitoring.view">
                              <LiveQueueStats groupId={g.id} />
                            </Can>
                            <IntegrationMiniList items={assigned} emptyText="No integrations assigned yet." />
                          </div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
