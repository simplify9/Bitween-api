import { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Handshake, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { formatDate } from "../../lib/dates";

export function PartnersPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";

  const partners = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });

  const setParam = (key: string, value: string | null) =>
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        if (value) next.set(key, value);
        else next.delete(key);
        return next;
      },
      { replace: key === "q" },
    );

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase();
    return (partners.data ?? []).filter((p) => !needle || p.name.toLowerCase().includes(needle));
  }, [partners.data, q]);

  return (
    <div>
      <PageHeader
        title="Partners"
        description="The external parties you exchange data with — their connection properties and API keys."
        actions={
          <Can permission="partners.create">
            <Button variant="primary" onClick={() => navigate("/partners/new")}>
              <Plus className="size-4" /> New partner
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
          placeholder="Search partners"
          aria-label="Search partners"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {partners.isPending ? (
        <LoadingBlock label="Loading partners…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<Handshake />} title={q ? "No partners match" : "No partners yet"}>
          {q ? "Try a different search." : "Create the first partner you exchange data with."}
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-140 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-2.5 font-medium">Partner</th>
                <th className="px-4 py-2.5 font-medium">Properties</th>
                <th className="px-4 py-2.5 font-medium">API keys</th>
                <th className="px-4 py-2.5 font-medium">Used by</th>
                <th className="px-4 py-2.5 font-medium">Since</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((p) => (
                <tr
                  key={p.id}
                  onClick={() => navigate(`/partners/${p.id}`)}
                  className="cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50"
                >
                  <td className="px-4 py-3">
                    <span className="flex items-center gap-2 font-medium text-ink-900">
                      {p.name}
                      {p.isSystem && <Badge tone="ink">Built-in</Badge>}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-ink-600">{p.propertyKeys.length}</td>
                  <td className="px-4 py-3 text-ink-600">{p.credentialCount}</td>
                  <td className="px-4 py-3 text-ink-600">
                    {p.usedByCount > 0 ? (
                      `${p.usedByCount} place${p.usedByCount === 1 ? "" : "s"}`
                    ) : (
                      <span className="text-ink-400">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-ink-500">{formatDate(p.createdOn)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

    </div>
  );
}
