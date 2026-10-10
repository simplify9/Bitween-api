import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Database, Search, Table2 } from "lucide-react";
import { keys } from "../../api/queryKeys";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Select, TextInput } from "../../components/ui/forms";
import { Panel } from "../../components/ui/Panel";
import {
  dialectOf,
  draftNameFor,
  draftStatementFor,
  fetchCapabilities,
  fetchSchemaObject,
  fetchSchemaPage,
  groupBySchema,
  type DbObject,
  type SqlDialect,
} from "./schema";

/** How many objects one page asks for. The adapter clamps anything above 1000. */
const PAGE = 200;

/**
 * What is actually in the database.
 *
 * This replaces reading `Discover` as raw JSON, which worked in the sense that the answer was on
 * screen: 1,812 lines of it on the first real database we pointed it at. Nobody finds a column
 * that way. What an operator is doing here is one of two things — checking a name they half
 * remember, or writing a statement against a table they have never seen — and both need search,
 * grouping, and columns on demand.
 *
 * Three things are deliberately not done here:
 *
 * - **No client-side filtering.** The search box sends `nameLike` to the database, so it searches
 *   the whole catalog rather than the page in hand. Filtering the page would quietly search a
 *   200-row window and report "nothing found" about a table that is there.
 * - **No columns up front.** The adapter refuses to make that the default, and it is right: a
 *   thousand tables' columns is not a menu, it is a download. They arrive when a row is opened.
 * - **No writes.** Everything reaches the adapter through `Inspect`, whose allow-list holds only
 *   Describe, Discover and GetStats. Query and Execute are not on it, and must not be — a
 *   View-level read must not become a way to run SQL on a customer's database.
 */
export function SchemaBrowser({
  dataSourceId,
  onUseInStatement,
  notStartedYet = false,
}: {
  dataSourceId: number;
  /**
   * The connection has never run, so there is nobody to ask: said plainly instead of the red
   * "not running" the server answers, which on a data source just created read as a fault.
   */
  notStartedYet?: boolean;
  /** Hands a generated statement to the statements panel. Absent when the user cannot create one. */
  onUseInStatement?: (draft: { name: string; sql: string; description: string }) => void;
}) {
  const [objectType, setObjectType] = useState("table");
  const [schema, setSchema] = useState("");
  const [search, setSearch] = useState("");
  const [nameLike, setNameLike] = useState("");
  const [page, setPage] = useState(0);

  // Typing sends one request per pause, not one per keystroke: each is a catalog query against
  // the customer's database, on the same pooled connection that is serving traffic.
  useEffect(() => {
    const timer = setTimeout(() => {
      setNameLike(search.trim());
      setPage(0);
    }, 350);
    return () => clearTimeout(timer);
  }, [search]);

  const capabilities = useQuery({
    queryKey: keys.dataSources.capabilities(dataSourceId),
    queryFn: () => fetchCapabilities(dataSourceId),
    staleTime: 5 * 60_000,
    retry: false,
    enabled: !notStartedYet,
  });

  const query = { objectType, schema, nameLike, skip: page * PAGE, take: PAGE };

  const objects = useQuery({
    queryKey: keys.dataSources.schema(dataSourceId, query),
    queryFn: () => fetchSchemaPage(dataSourceId, query),
    // A catalog does not change under you mid-session, and every refetch is a query on the
    // connection that is serving traffic.
    staleTime: 60_000,
    retry: false,
    enabled: !notStartedYet,
  });

  // Which tabs to show is the engine's answer, not a list this file keeps: Oracle has packages,
  // PostgreSQL has materialised views, and hardcoding either would offer one to the other.
  const types = capabilities.data?.supportedObjects ?? ["table", "view"];

  // Only the schemas in the page, so this narrows what is on screen rather than pretending to
  // know every schema in the database — the catalog is paged and this page may not hold them all.
  const groups = useMemo(() => groupBySchema(objects.data?.objects ?? []), [objects.data]);
  const schemasHere = groups.map((g) => g.schema);

  const rows = objects.data?.objects ?? [];
  const hasMore = objects.data?.hasMore ?? false;

  // How to write a placeholder and a row limit for THIS engine. Reported by the adapter rather
  // than guessed from the label, so a draft cannot use a prefix the connection will refuse.
  const dialect = dialectOf(capabilities.data);

  return (
    <Panel
      title="What is in the database"
      description="The catalog, as the connection can see it. Read-only: this reads names and types, and writes nothing."
    >
      {capabilities.data && (
        <p className="mb-3 text-[12px] text-ink-500">
          {capabilities.data.engine} {capabilities.data.serverVersion}
          {!capabilities.data.schemaDiscovery &&
            " — this engine does not report its catalog, so nothing will be listed."}
        </p>
      )}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="relative min-w-56 flex-1">
          <Search className="absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-ink-400" />
          <TextInput
            className="pl-8"
            value={search}
            placeholder="Search names…"
            aria-label="Search the catalog by name"
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>

        <Select
          aria-label="Object type"
          value={objectType}
          options={types.map((t) => ({ value: t, label: LABELS[t] ?? t }))}
          onChange={(e) => {
            setObjectType(e.target.value);
            setPage(0);
          }}
        />

        <Select
          aria-label="Schema"
          value={schema}
          options={[
            { value: "", label: "Every schema" },
            ...schemasHere.map((s) => ({ value: s, label: s })),
            // Kept selectable after a search narrows the page past it, so choosing a schema and
            // then typing does not silently drop the filter that is still in force.
            ...(schema && !schemasHere.includes(schema) ? [{ value: schema, label: schema }] : []),
          ]}
          onChange={(e) => {
            setSchema(e.target.value);
            setPage(0);
          }}
        />
      </div>

      {notStartedYet && (
        <p className="rounded-lg bg-ink-50 px-3 py-2 text-[13px] text-ink-600">
          Once the connection has started, what the database holds is listed here.
        </p>
      )}
      {objects.isError && (
        <FormError>
          {objects.error instanceof Error
            ? objects.error.message
            : "The catalog could not be read."}
        </FormError>
      )}

      {objects.isLoading && <LoadingBlock label="Reading the catalog…" />}

      {objects.data && rows.length === 0 && (
        <p className="py-3 text-[13px] text-ink-500">
          {nameLike
            ? `Nothing here is called “${nameLike}”. The search matches anywhere in the name, and the database does the matching — so this is the whole catalog's answer, not just this page's.`
            : "This connection can see no objects of this type. That may be the schema it is pointed at, or what its role is granted."}
        </p>
      )}

      {rows.length > 0 && (
        <div className="max-h-[28rem] overflow-y-auto rounded-lg border border-ink-100">
          {groups.map((group) => (
            <div key={group.schema}>
              <div className="sticky top-0 z-10 flex items-center gap-2 border-b border-ink-100 bg-ink-50/95 px-3 py-1.5 backdrop-blur">
                <Database className="size-3.5 text-ink-400" />
                <span className="font-mono text-[12px] font-semibold text-ink-700">
                  {group.schema}
                </span>
                <Badge tone="neutral">{group.objects.length}</Badge>
              </div>
              <ul className="divide-y divide-ink-100">
                {group.objects.map((object) => (
                  <ObjectRow
                    key={`${object.schema}.${object.name}`}
                    dataSourceId={dataSourceId}
                    object={object}
                    dialect={dialect}
                    onUseInStatement={onUseInStatement}
                  />
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      {(page > 0 || hasMore) && (
        <div className="mt-3 flex items-center gap-2">
          <Button size="sm" disabled={page === 0} onClick={() => setPage(page - 1)}>
            Previous
          </Button>
          <Button size="sm" disabled={!hasMore} onClick={() => setPage(page + 1)}>
            Next
          </Button>
          <span className="text-[12px] text-ink-500">
            {/* A count, not a total: the catalog is never counted, only paged — see the adapter's
                "never SELECT COUNT(*)". Claiming "1–200 of 4,000" would be inventing the 4,000. */}
            Showing {page * PAGE + 1}–{page * PAGE + rows.length}
            {hasMore ? ", and there are more" : ""}
          </span>
        </div>
      )}
    </Panel>
  );
}

const LABELS: Record<string, string> = {
  table: "Tables",
  view: "Views",
  materialized_view: "Materialised views",
  procedure: "Procedures",
  function: "Functions",
  sequence: "Sequences",
  package: "Packages",
  synonym: "Synonyms",
};

/**
 * One object, closed. Opening it fetches that object alone — the only way to see columns without
 * asking for every table's at once.
 */
function ObjectRow({
  dataSourceId,
  object,
  dialect,
  onUseInStatement,
}: {
  dataSourceId: number;
  object: DbObject;
  dialect: SqlDialect;
  onUseInStatement?: (draft: { name: string; sql: string; description: string }) => void;
}) {
  const [open, setOpen] = useState(false);

  const detail = useQuery({
    queryKey: keys.dataSources.schemaObject(
      dataSourceId,
      object.type,
      object.schema,
      object.name,
    ),
    queryFn: () => fetchSchemaObject(dataSourceId, object.type, object.schema, object.name),
    enabled: open,
    staleTime: 5 * 60_000,
    retry: false,
  });

  // The listed object until its detail arrives, so the header does not flicker and "use this"
  // still works on a row whose columns are still loading — it just writes `select *`.
  const full = detail.data ?? object;

  return (
    <li>
      <div className="flex items-center gap-2 px-3 py-1.5 hover:bg-ink-50/60">
        <button
          type="button"
          className="flex min-w-0 flex-1 items-center gap-2 text-left"
          onClick={() => setOpen(!open)}
          aria-expanded={open}
        >
          {open ? (
            <ChevronDown className="size-3.5 shrink-0 text-ink-400" />
          ) : (
            <ChevronRight className="size-3.5 shrink-0 text-ink-400" />
          )}
          <Table2 className="size-3.5 shrink-0 text-ink-400" />
          <span className="truncate font-mono text-[13px] text-ink-900">{object.name}</span>
          {object.rowCount != null && (
            <Badge tone="neutral" title="An estimate from the catalog, not a counted total.">
              ~{object.rowCount.toLocaleString()}
            </Badge>
          )}
          {object.comment && (
            <span className="truncate text-[12px] text-ink-500">{object.comment}</span>
          )}
        </button>

        {onUseInStatement && (
          <Button
            size="sm"
            variant="ghost"
            onClick={() =>
              onUseInStatement({
                name: draftNameFor(full),
                sql: draftStatementFor(full, dialect),
                description: `Generated from ${full.schema}.${full.name}.`,
              })
            }
          >
            Use in a statement
          </Button>
        )}
      </div>

      {open && (
        <div className="border-t border-ink-100 bg-ink-50/40 px-3 py-2 pl-8">
          {detail.isLoading && <span className="text-[12px] text-ink-500">Reading columns…</span>}

          {detail.isError && (
            <span className="text-[12px] text-danger-700">
              {detail.error instanceof Error ? detail.error.message : "Could not read this object."}
            </span>
          )}

          {detail.data && detail.data.columns.length > 0 && (
            <table className="w-full text-[12px]">
              <tbody>
                {[...detail.data.columns]
                  .sort((a, b) => a.ordinal - b.ordinal)
                  .map((column) => (
                  <tr key={column.name}>
                    <td className="py-0.5 pr-3 font-mono text-ink-800">
                      {column.name}
                      {column.primaryKey && (
                        <span className="ml-1 text-ink-400" title="Part of the primary key">
                          pk
                        </span>
                      )}
                    </td>
                    <td className="py-0.5 pr-3 font-mono text-ink-500">{column.dbType}</td>
                    <td className="py-0.5 text-ink-400">
                      {/* Generated is worth saying out loud: it is the reason an insert that
                          supplies this column is rejected. */}
                      {column.generated
                        ? "database fills this in"
                        : column.nullable
                          ? "null allowed"
                          : "not null"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {detail.data && detail.data.parameters.length > 0 && (
            <table className="w-full text-[12px]">
              <tbody>
                {detail.data.parameters.map((parameter, i) => (
                  <tr key={`${parameter.name}-${i}`}>
                    <td className="py-0.5 pr-3 font-mono text-ink-800">
                      {parameter.name || <span className="text-ink-400">(unnamed)</span>}
                    </td>
                    <td className="py-0.5 pr-3 font-mono text-ink-500">{parameter.dbType}</td>
                    <td className="py-0.5 text-ink-400">{parameter.direction}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {detail.data &&
            detail.data.columns.length === 0 &&
            detail.data.parameters.length === 0 && (
              <span className="text-[12px] text-ink-500">
                Nothing to show — this object reports no columns or parameters.
              </span>
            )}

          {detail.data === null && (
            <span className="text-[12px] text-ink-500">
              It is no longer in the catalog. Someone dropped it, or the role lost sight of it.
            </span>
          )}
        </div>
      )}
    </li>
  );
}
