import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { Check, Plus, Trash2 } from "lucide-react";
import { api, type DataSourceStatement } from "../../api";
import { keys } from "../../api/queryKeys";
import { Can } from "../../auth/guards";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { fetchCapabilities } from "./schema";

/**
 * The SQL this connection is allowed to run.
 *
 * It lives here, on the data source, rather than as its own page in the sidebar — a statement is
 * only ever reached through the connection it runs against, the same reason data sources sit under
 * bus gateways in the nav.
 *
 * But it is NOT gated on the data source's own permission. Writing a query and rotating a database
 * password are different jobs, and `data-sources.edit` is the one that changes credentials — so
 * these controls answer to `data-source-statements.*` instead. That separation is the whole reason
 * a statement is a record rather than a field.
 */
export function Statements({
  dataSourceId,
  seed,
  onSeedConsumed,
}: {
  dataSourceId: number;
  /**
   * A statement written for the operator by the schema browser, waiting to be reviewed. It opens
   * the form rather than saving anything: generated SQL is a starting point, and the name and the
   * row limit are exactly the parts worth changing before it becomes a record with an audit trail.
   */
  seed?: StatementSeed | null;
  onSeedConsumed?: () => void;
}) {
  const queryClient = useQueryClient();
  const canCreate = useSessionCan("data-source-statements.create");
  const canEdit = useSessionCan("data-source-statements.edit");

  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<DataSourceStatement | null>(null);

  // A seed arriving means the operator pressed "use in a statement" somewhere below; the form has
  // to be open for them to see what it wrote.
  useEffect(() => {
    if (seed) setAdding(true);
  }, [seed]);

  const statements = useQuery({
    queryKey: keys.dataSourceStatements.forDataSource(dataSourceId),
    queryFn: () => api.listDataSourceStatements(dataSourceId),
    retry: false,
  });

  const invalidate = () =>
    queryClient.invalidateQueries({
      queryKey: keys.dataSourceStatements.forDataSource(dataSourceId),
    });

  const remove = useMutation({
    mutationFn: (id: number) => api.deleteDataSourceStatement(id),
    onSuccess: () => {
      setRemoving(null);
      setError(null);
      void invalidate();
    },
    // The server refuses while anything names it, and the refusal lists what. That message is the
    // useful part, so it is shown as-is rather than replaced with something generic.
    onError: (e: Error) => setError(e.message),
  });

  if (statements.isLoading) return <LoadingBlock />;

  const rows = statements.data ?? [];

  return (
    <Panel
      title="SQL statements"
      description="The named SQL this connection may run. A subscription names one; the SQL never travels with a message."
      action={
        <Can permission="data-source-statements.create">
          <Button variant="secondary" onClick={() => setAdding(true)}>
            <Plus className="size-4" /> Add statement
          </Button>
        </Can>
      }
    >
      {error && <div className="mx-4 mb-3"><FormError>{error}</FormError></div>}

      {/* Padded to the panel's own gutter: the form is a direct child of Panel, which only pads
          its header, so without this it sits flush against the border while every row below is
          inset. */}
      {adding && canCreate && (
        <div className="px-4 pb-3">
          <StatementForm
            // Remounts when a different object is picked, so the fields take the new draft
            // instead of keeping what the previous one seeded.
            key={seed ? `${seed.name}:${seed.sql}` : "blank"}
            dataSourceId={dataSourceId}
            seed={seed}
            onClose={() => {
              setAdding(false);
              onSeedConsumed?.();
            }}
            onSaved={() => {
              setAdding(false);
              onSeedConsumed?.();
              void invalidate();
            }}
          />
        </div>
      )}

      {rows.length === 0 ? (
        <p className="px-4 pb-4 text-[13px] text-ink-500">
          None yet. Until one exists, a subscription bound to this connection has nothing it is
          allowed to run — the adapter refuses SQL sent with a message unless ad-hoc SQL is
          explicitly switched on.
        </p>
      ) : (
        <ul className="divide-y divide-ink-100 border-t border-ink-100">
          {rows.map((statement) => (
            <StatementRow
              key={statement.id}
              statement={statement}
              canEdit={canEdit}
              onDelete={() => {
                setError(null);
                setRemoving(statement);
              }}
              onSaved={invalidate}
            />
          ))}
        </ul>
      )}

      {removing && (
        <ConfirmDialog
          title={`Delete ${removing.name}?`}
          confirmLabel="Delete"
          body={
            removing.usageCount > 0
              ? `${removing.usageCount} subscription(s) name this statement. The server will refuse — mark it inactive instead, to retire it while they are migrated.`
              : "Nothing names this statement, so removing it changes no running integration."
          }
          onClose={() => setRemoving(null)}
          onConfirm={() => remove.mutateAsync(removing.id).then(() => undefined)}
        />
      )}
    </Panel>
  );
}

/**
 * One statement. The usage count is the column that matters: it is the difference between SQL that
 * is dead and SQL that is merely quiet, and it is what stopped anyone ever tidying up the JSON
 * field this replaced.
 */
function StatementRow({
  statement,
  canEdit,
  onDelete,
  onSaved,
}: {
  statement: DataSourceStatement;
  canEdit: boolean;
  onDelete: () => void;
  onSaved: () => void;
}) {
  const [open, setOpen] = useState(false);

  const usage = useQuery({
    queryKey: keys.dataSourceStatements.usage(statement.id),
    queryFn: () => api.getDataSourceStatementUsage(statement.id),
    // Only when the row is expanded: the answer costs a scan of this connection's subscriptions,
    // and a list of forty statements would run forty of them on first paint.
    enabled: open,
    retry: false,
  });

  return (
    <li className="px-4 py-3">
      <div className="flex items-start justify-between gap-3">
        <button
          type="button"
          className="min-w-0 flex-1 text-left"
          onClick={() => setOpen(!open)}
          aria-expanded={open}
        >
          <span className="flex flex-wrap items-center gap-2">
            <code className="text-[13px] font-semibold text-ink-900">{statement.name}</code>
            {statement.inactive && <Badge tone="warn">Inactive</Badge>}
            <Badge tone={statement.usageCount > 0 ? "ink" : "neutral"}>
              {statement.usageCount === 0
                ? "Unused"
                : `${statement.usageCount} subscription${statement.usageCount === 1 ? "" : "s"}`}
            </Badge>
            {statement.workGroupName && <Badge tone="neutral">{statement.workGroupName}</Badge>}
          </span>
          {statement.description && (
            <p className="mt-0.5 text-[13px] text-ink-500">{statement.description}</p>
          )}
          <code className="mt-1 block truncate text-[12px] text-ink-500">{statement.sql}</code>
        </button>

        <Can permission="data-source-statements.delete">
          <Button variant="ghost" aria-label={`Delete ${statement.name}`} onClick={onDelete}>
            <Trash2 className="size-4" />
          </Button>
        </Can>
      </div>

      {open && (
        <div className="mt-3 space-y-3 border-t border-ink-100 pt-3">
          <pre className="overflow-x-auto rounded-lg bg-ink-50 p-3 text-[12px] text-ink-800">
            {statement.sql}
          </pre>

          <div className="text-[12px] text-ink-500">
            {usage.isLoading && "Checking what uses this…"}
            {usage.data && usage.data.usedBy.length === 0 && (
              <span>
                Nothing names it. Safe to change or delete — though a subscription could be added
                tomorrow, so a name that reads like what it does is still worth having.
              </span>
            )}
            {usage.data && usage.data.usedBy.length > 0 && (
              <>
                <span className="block font-medium text-ink-700">Used by</span>
                <ul className="mt-1 space-y-0.5">
                  {usage.data.usedBy.map((entry) => (
                    <li key={`${entry.subscriptionId}-${entry.role}`}>
                      <Link
                        className="text-accent-600 hover:underline"
                        to={`/subscriptions/${entry.subscriptionId}`}
                      >
                        {entry.subscriptionName}
                      </Link>{" "}
                      — {entry.role}, {entry.operation}
                      {entry.inactive && " (inactive)"}
                    </li>
                  ))}
                </ul>
                <p className="mt-1">
                  Renaming is refused while any of these name it: a rename would break them, and
                  nothing on this screen can repair that. Changing the SQL is allowed.
                </p>
              </>
            )}
          </div>

          {canEdit && (
            <StatementForm
              statement={statement}
              dataSourceId={statement.dataSourceId}
              onClose={() => setOpen(false)}
              onSaved={onSaved}
            />
          )}
        </div>
      )}
    </li>
  );
}

/** A statement the schema browser wrote, for the operator to review before it is saved. */
export interface StatementSeed {
  name: string;
  sql: string;
  description: string;
}

function StatementForm({
  statement,
  seed,
  dataSourceId,
  onClose,
  onSaved,
}: {
  statement?: DataSourceStatement;
  seed?: StatementSeed | null;
  dataSourceId: number;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(statement?.name ?? seed?.name ?? "");
  const [sql, setSql] = useState(statement?.sql ?? seed?.sql ?? "");
  const [description, setDescription] = useState(
    statement?.description ?? seed?.description ?? "",
  );
  const [inactive, setInactive] = useState(statement?.inactive ?? false);
  // What this engine spells a placeholder with, reported by the adapter. Named rather than listed
  // per engine: there are four now, and a hint that names two of them is wrong for the others.
  const capabilities = useQuery({
    queryKey: keys.dataSources.capabilities(dataSourceId),
    queryFn: () => fetchCapabilities(dataSourceId),
    staleTime: 5 * 60_000,
    retry: false,
  });
  const prefix = capabilities.data?.parameterPrefix || "@";

  const [cursorColumn, setCursorColumn] = useState(statement?.cursorColumn ?? "");
  const [keyColumn, setKeyColumn] = useState(statement?.keyColumn ?? "");

  // Its own error, shown under the buttons rather than raised to the panel header. A message
  // about the SQL in this box belongs beside the box, not four rows above it where a long list
  // of statements can put it off screen entirely.
  const [error, setError] = useState<string | null>(null);

  // The codebase's existing way of saying a save landed — see the mapping editor. Two seconds,
  // in place of the button, because a save that changes nothing visible on the form otherwise
  // looks like a button that did nothing.
  const [justSaved, setJustSaved] = useState(false);

  // True when the database never looked at the SQL — the adapter for this connection is not
  // running, so there was nobody to ask. Said out loud because a save that reported nothing would
  // look exactly like one that had been verified, and the next thing to notice would be a failed
  // connection test or a failed message.
  const [unchecked, setUnchecked] = useState(false);

  const save = useMutation({
    mutationFn: async () => {
      if (statement) {
        return api.updateDataSourceStatement(statement.id, {
          name,
          sql,
          description,
          workGroupId: statement.workGroupId,
          inactive,
          cursorColumn,
          keyColumn,
        });
      }

      return api.createDataSourceStatement(dataSourceId, {
        name,
        sql,
        description,
        cursorColumn,
        keyColumn,
      });
    },
    onSuccess: (saved) => {
      setError(null);
      setUnchecked(!saved.checked);
      // Only for an edit. A create closes the form on success, which says it landed by itself —
      // and a "Saved" flash on a form that is disappearing is a flicker, not a message.
      if (statement) {
        setJustSaved(true);
        setTimeout(() => setJustSaved(false), 2000);
      }
      onSaved();
    },
    // Not raised to the panel as well: one message in two places reads as two problems, and
    // the panel header is where a DELETE failure belongs — that one has no form to sit under.
    onError: (e: Error) => setError(e.message),
  });

  const complete = name.trim().length > 0 && sql.trim().length > 0;

  return (
    <div className="space-y-3 rounded-lg border border-ink-200 bg-ink-50/50 p-3">
      <Field
        label="Name"
        hint="What a subscription names to run it — its Statement property, or ReceiveStatement for one that polls. Unique per connection, and matched without regard to case."
      >
        <TextInput value={name} onChange={(e) => setName(e.target.value)} />
      </Field>

      <Field
        label="SQL"
        hint={
          `Bind values as parameters — ${prefix}name on this connection. Never concatenate a value `
          + `into the text.`
        }
      >
        <textarea
          className="min-h-28 w-full rounded-lg border border-ink-200 bg-white px-3 py-2 font-mono text-[13px] text-ink-900"
          value={sql}
          onChange={(e) => setSql(e.target.value)}
          spellCheck={false}
        />
      </Field>

      <Field label="Description" hint="Why it exists, for whoever inherits it.">
        <TextInput value={description} onChange={(e) => setDescription(e.target.value)} />
      </Field>

      {/*
        Always shown, never behind a disclosure. They were, and it failed exactly as a disclosure
        does: a subscription bound to this statement warns "set its key column on the connection",
        and whoever followed that here found a line of prose instead of the field. Two optional
        boxes with a caption is the cheaper mistake.
      */}
      <div className="grid gap-3 border-t border-ink-200 pt-3 sm:grid-cols-2">
        <p className="text-[12px] font-medium text-ink-600 sm:col-span-2">
          Only for a statement a receiver polls with
        </p>

        <Field
          label="Key column"
          hint="Which column identifies a row, for mark-processed and deduplication. Required for polling; the statement has to select it, spelled as the database returns it."
        >
          <TextInput
            value={keyColumn}
            onChange={(e) => setKeyColumn(e.target.value)}
            spellCheck={false}
            placeholder="id"
          />
        </Field>

        <Field
          label="Cursor column"
          hint="Which column the receiver follows — the incrementing id, or the modified-at timestamp. Its value in the last row read is what gets saved. Not needed for bulk or marker."
        >
          <TextInput
            value={cursorColumn}
            onChange={(e) => setCursorColumn(e.target.value)}
            spellCheck={false}
            placeholder="id"
          />
        </Field>

        {/* Said here rather than in the docs: both columns describe what THIS query returns, so
            every subscription polling it reads them the same way. Which is the point of their
            being here and not on the subscription. */}
        <p className="text-[12px] text-ink-500 sm:col-span-2">
          These describe the rows this statement returns, so every subscription polling it agrees
          on them. How often, in what mode and in what batch size is each subscription&apos;s own
          choice.
        </p>

        {cursorColumn && !/[:@]cursor\b/i.test(sql) && (
          <p className="text-[12px] text-warn-700 sm:col-span-2">
            The SQL never mentions the cursor, so every poll would re-read from the beginning.
            A cursor mode needs something like{" "}
            <code>where {cursorColumn} &gt; @cursor order by {cursorColumn}</code>.
          </p>
        )}
      </div>

      {statement && (
        <Checkbox
          checked={inactive}
          onChange={(e) => setInactive(e.target.checked)}
          label="Inactive"
          description="Kept, but dropped from what the adapter will run. A subscription still naming it fails loudly, which is the point of retiring rather than deleting."
        />
      )}

      {/* Under the fields and above the buttons: the error is about what was typed, and this is
          where the eye already is when the save is pressed. */}
      {error && <FormError>{error}</FormError>}

      {unchecked && !error && (
        <p className="text-[12px] text-warn-700">
          Saved, but not checked. This connection is not running here, so the database never saw
          this SQL — run a connection test once it is up.
        </p>
      )}

      <div className="flex items-center gap-2">
        {justSaved ? (
          <span className="flex items-center gap-1 text-sm font-medium text-ok-600">
            <Check className="size-4" /> Saved
          </span>
        ) : (
          <Button onClick={() => save.mutate()} disabled={!complete || save.isPending} busy={save.isPending}>
            {statement ? "Save changes" : "Create statement"}
          </Button>
        )}
        <Button variant="ghost" onClick={onClose}>
          {justSaved ? "Close" : "Cancel"}
        </Button>
      </div>
    </div>
  );
}
