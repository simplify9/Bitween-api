import { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { FileText, Plus, Search } from "lucide-react";
import { api } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, LoadingBlock } from "../../components/ui/basics";
import { CodeBadge } from "../../components/ui/Panel";

export function InformationTypesPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const q = searchParams.get("q") ?? "";

  const types = useQuery({ queryKey: ["information-types"], queryFn: () => api.listInformationTypes() });

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
    return (types.data ?? []).filter(
      (t) =>
        !needle || t.name.toLowerCase().includes(needle) || (t.code ?? "").toLowerCase().includes(needle),
    );
  }, [types.data, q]);

  return (
    <div>
      <PageHeader
        title="Information types"
        description="The kinds of business documents flowing between you and your partners — each with a short code used across the system."
        help={{
          title: "How information types work",
          body: (
            <>
              <p>
                Every exchange carries exactly one information type (a purchase order, a shipment
                update…). The <strong>code</strong> is its short identity everywhere in Bitween;
                the name is the friendly label.
              </p>
              <p>
                <strong>Promoted properties</strong> pull values out of the payload (by JSON path
                or XML path) so routes and filters can match on them — for example routing orders
                by store number.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="documents.create">
            <Button variant="primary" onClick={() => navigate("/information-types/new")}>
              <Plus className="size-4" /> New information type
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
          placeholder="Search by name or code"
          aria-label="Search information types"
          className="h-9 w-full rounded-lg border border-ink-200 bg-white pr-3 pl-9 text-sm placeholder:text-ink-400 focus:border-crimson-400 focus:ring-2 focus:ring-crimson-100 focus:outline-none"
        />
      </div>

      {types.isPending ? (
        <LoadingBlock label="Loading information types…" />
      ) : filtered.length === 0 ? (
        <EmptyState icon={<FileText />} title={q ? "No information types match" : "No information types yet"}>
          {q ? "Try a different search." : "Define the first kind of document your integrations will carry."}
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-150 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-2.5 font-medium">Code</th>
                <th className="px-4 py-2.5 font-medium">Name</th>
                <th className="px-4 py-2.5 font-medium">Format</th>
                <th className="px-4 py-2.5 font-medium">Bus</th>
                <th className="px-4 py-2.5 font-medium">Promoted</th>
                <th className="px-4 py-2.5 font-medium">Used by</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((t) => (
                <tr
                  key={t.id}
                  onClick={() => navigate(`/information-types/${t.id}`)}
                  className="cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50"
                >
                  <td className="px-4 py-3">
                    <CodeBadge code={t.code} name={t.name} />
                  </td>
                  <td className="px-4 py-3 font-medium text-ink-900">{t.name}</td>
                  <td className="px-4 py-3">
                    <Badge>{t.format.toUpperCase()}</Badge>
                  </td>
                  <td className="px-4 py-3">
                    {t.busEnabled ? (
                      <code className="font-mono text-xs text-ink-600">{t.busMessageTypeName}</code>
                    ) : (
                      <span className="text-ink-400">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3 text-ink-600">{t.promotedProperties.length}</td>
                  <td className="px-4 py-3 text-ink-600">
                    {t.usedByCount > 0 ? (
                      `${t.usedByCount} place${t.usedByCount === 1 ? "" : "s"}`
                    ) : (
                      <span className="text-ink-400">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

    </div>
  );
}
