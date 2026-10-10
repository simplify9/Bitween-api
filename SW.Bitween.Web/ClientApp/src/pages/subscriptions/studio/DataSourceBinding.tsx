import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router";
import { Check } from "lucide-react";
import { api, type DataSourceStatement } from "../../../api";
import { keys } from "../../../api/queryKeys";
import { Field, Select, TextInput } from "../../../components/ui/forms";
import { Button, FormError } from "../../../components/ui/basics";
import { useSessionCan } from "../../../auth/useSessionCan";

/**
 * Binds an adapter slot to a database: which connection, which statement, and what to do with it.
 *
 * Shown only when the slot's adapter is a relational data source provider, because for anything
 * else there is nothing here to choose. It writes two things, in two places, and the split is the
 * point:
 *
 * - The CONNECTION is the subscription's (`dataSourceId`). One per subscription — several slots
 *   pointed at the same database share one warm pool, which is the whole reason the pool is held
 *   by a long-lived process.
 * - The STATEMENT and OPERATION are this SLOT's adapter properties, so a subscription's handler
 *   and its receiver can run different statements through the same connection.
 *
 * What it never offers is a box to type SQL into. Adapter property values have `{{partner.X}}`
 * substituted into them before the adapter sees them, so SQL here would be steerable by ordinary
 * partner data. The statement is a name; the SQL lives on the data source, under its own permission.
 */
export function DataSourceBinding({
  dataSourceId,
  properties,
  onDataSourceChange,
  onPropertiesChange,
  disabled,
  slot,
  siblings,
}: {
  dataSourceId: number | null;
  properties: Record<string, string>;
  onDataSourceChange: (id: number | null) => void;
  onPropertiesChange: (properties: Record<string, string>) => void;
  disabled?: boolean;
  /** Named in the hints, so it is clear which stage's statement is being chosen. */
  slot: "handler" | "mapper" | "receiver";
  /**
   * What the OTHER stages are set to. The connection belongs to the subscription, not to a stage,
   * so choosing one here chooses it for all of them — and a stage whose adapter cannot use the
   * kind that was picked is a subscription that saves cleanly and fails on its first message.
   */
  siblings?: { slot: string; adapterId: string | null }[];
}) {
  const sources = useQuery({
    queryKey: keys.dataSources.list,
    queryFn: () => api.listDataSources(),
    staleTime: 60_000,
  });

  // The chosen connection's own settings, for one question: does it actually define a default
  // receive mode? The dropdown used to offer "the connection's default" whether or not one
  // existed, and a subscription that took it failed at poll time with "no ReceiveMode" — a fault
  // reported nowhere near the screen that caused it.
  const chosenDetail = useQuery({
    queryKey: keys.dataSources.detail(dataSourceId ?? 0),
    queryFn: () => api.getDataSource(dataSourceId!),
    enabled: dataSourceId != null && slot === "receiver",
    staleTime: 60_000,
  });

  const statements = useQuery({
    queryKey: keys.dataSourceStatements.forDataSource(dataSourceId ?? 0),
    queryFn: () => api.listDataSourceStatements(dataSourceId!),
    enabled: dataSourceId != null,
    staleTime: 30_000,
  });

  // A relational source runs SQL; a broker source is published to. Both bind here, and which one
  // you are looking at decides the whole lower half of this panel.
  //
  // A broker is offered in a DELIVERY only. Ingress from a customer's broker comes through a bus
  // gateway, not through a subscription's receiver — offering it there would be offering
  // something that cannot work.
  const usable = (sources.data ?? []).filter(
    (s) => s.kind === "Relational" || (s.kind === "Broker" && slot !== "receiver"),
  );
  const chosen = usable.find((s) => s.id === dataSourceId) ?? null;
  const broker = chosen?.kind === "Broker";

  // A sibling stage whose adapter is bound to the connection but cannot use the kind that was
  // chosen. A relational adapter needs a database; a bus adapter needs a broker.
  const conflicting = (siblings ?? []).filter((sibling) => {
    if (!chosen || !sibling.adapterId) return false;
    const wantsBroker = sibling.adapterId.startsWith("bitween.bus.");
    const wantsDatabase = sibling.adapterId.startsWith("bitween.db.");
    if (!wantsBroker && !wantsDatabase) return false;
    return wantsBroker ? chosen.kind !== "Broker" : chosen.kind !== "Relational";
  });

  const setProperty = (name: string, value: string) => {
    const next = { ...properties };
    if (value === "") delete next[name];
    else next[name] = value;
    onPropertiesChange(next);
  };

  const available = (statements.data ?? []).filter((s) => !s.inactive);

  // A receiver names its statement under a different key, and it has to be the one the adapter
  // reads: a receiver polls with ReceiveStatement, while every other slot runs Statement. Writing
  // the wrong one saves cleanly and then does nothing, which is the worst way for this to fail.
  const statementKey = slot === "receiver" ? "ReceiveStatement" : "Statement";
  const current = properties[statementKey] ?? "";

  // A statement named here but gone from the connection is the case worth surfacing: it fails at
  // run time as "not a statement this data source defines", which is a long way from this screen.
  const missing = current !== "" && !available.some((s) => s.name === current);

  const chosenStatement = available.find((s) => s.name === current) ?? null;

  // Case-insensitively, the way adapter properties bind.
  const connectionMode = Object.entries(chosenDetail.data?.properties ?? {}).find(
    ([key]) => key.toLowerCase() === "receivemode",
  )?.[1];

  const mode = properties.ReceiveMode ?? "";
  const needsCursor = ["incrementing", "timestamp", "timestamp+incrementing"].includes(mode);
  // Without a cursor, the mark statement is the only thing that stops a poll re-reading the same
  // rows for ever — so it is asked for rather than left to be discovered.
  const needsMark = mode === "bulk" || mode === "marker";

  return (
    <div className="space-y-3 rounded-lg border border-ink-200 bg-ink-50/50 p-3">
      <Field
        label="Connection"
        hint="The data source every stage of this subscription runs through — one per subscription, so choosing here chooses for the others too. Shared deliberately: the stages then use one pooled connection rather than opening their own."
      >
        <Select
          value={dataSourceId == null ? "" : String(dataSourceId)}
          disabled={disabled || sources.isLoading}
          options={[
            { value: "", label: "None — this adapter carries its own settings" },
            ...usable.map((s) => ({
              value: String(s.id),
              label: s.kind === "Broker" ? `${s.name} (broker)` : s.name,
            })),
          ]}
          onChange={(e) => {
            const next = e.target.value === "" ? null : Number(e.target.value);
            onDataSourceChange(next);

            // The statement belonged to the old connection; statement names are scoped to their
            // data source, so keeping it would point at something that may not exist here.
            if (next !== dataSourceId) setProperty(statementKey, "");
          }}
        />
      </Field>

      {/*
        The trap in one connection per subscription: a delivery that publishes to a broker and a
        source that reads from a database cannot both be served by it. Said at the moment of
        choosing, because the failure otherwise arrives on the first message with nothing on
        screen to connect it to this decision.
      */}
      {chosen && conflicting.length > 0 && (
        <p className="text-[12px] text-warn-700">
          This subscription&apos;s {conflicting.map((c) => c.slot).join(" and ")} also runs through
          its connection, and {chosen.name} is a {chosen.kind === "Broker" ? "broker" : "database"}
          {" "}— so {conflicting.length === 1 ? "that stage" : "those stages"} cannot use it. Chain
          a second subscription instead, with this one&apos;s response feeding it.
        </p>
      )}

      {usable.length === 0 && !sources.isLoading && (
        <p className="text-[12px] text-ink-500">
          No data sources exist that this stage can use yet.{" "}
          <Link className="text-accent-600 hover:underline" to="/data-sources">
            Create one
          </Link>{" "}
          and give it the SQL this subscription should run.
        </p>
      )}

      {chosen && broker && <BrokerDelivery connection={chosen.name} properties={properties} disabled={disabled} setProperty={setProperty} />}

      {chosen && !broker && (
        <>
          <Field
            label="Statement"
            hint={
              slot === "receiver"
                ? `Which of ${chosen.name}'s statements this receiver polls with. It also carries the columns that say which is the cursor and which identifies a row.`
                : `Which of ${chosen.name}'s statements this ${slot} runs. The SQL itself lives on the connection — a message supplies parameter values, never SQL.`
            }
          >
            <Select
              value={current}
              disabled={disabled || statements.isLoading}
              options={[
                { value: "", label: "None — the message must name one itself" },
                ...available.map((s) => ({
                  value: s.name,
                  label: s.description ? `${s.name} — ${s.description}` : s.name,
                })),
                // Kept selectable so choosing something else is a decision, not an accident.
                ...(missing ? [{ value: current, label: `${current} — no longer on this connection` }] : []),
              ]}
              onChange={(e) => setProperty(statementKey, e.target.value)}
            />
          </Field>

          {missing && (
            <p className="text-[12px] text-danger-700">
              “{current}” is not a statement {chosen.name} defines any more. This subscription will
              fail on its next message until it names one that exists.
            </p>
          )}

          {slot !== "receiver" && (
            <Field
              label="Operation"
              hint="query returns rows; execute reports what it changed; call runs a stored procedure."
            >
              <Select
                value={properties.Operation ?? "query"}
                disabled={disabled}
                options={[
                  { value: "query", label: "query" },
                  { value: "execute", label: "execute" },
                  { value: "call", label: "call" },
                ]}
                onChange={(e) => setProperty("Operation", e.target.value)}
              />
            </Field>
          )}

          {/*
            A receiver has no operation to pick — polling is the operation. What it has instead is
            a reading policy, and that is this subscription's, not the connection's: the same
            statement is legitimately read in bulk once for a backfill and incrementally after.

            What is NOT here is the cursor and key columns. They describe the rows the statement
            returns, so they live on the statement, where every subscription polling it agrees on
            them rather than each nominating its own.
          */}
          {slot === "receiver" && (
            <>
              <Field
                label="Receive mode"
                hint="How this subscription finds new rows. None of these can see a DELETE — that is a property of polling, not of this adapter."
              >
                <Select
                  value={properties.ReceiveMode ?? ""}
                  disabled={disabled}
                  options={[
                    {
                      value: "",
                      label: connectionMode
                        ? `The connection's default — ${connectionMode}`
                        : "None — and this connection sets no default",
                    },
                    { value: "incrementing", label: "incrementing — follow an always-growing column" },
                    { value: "timestamp", label: "timestamp — follow a modified-at column" },
                    {
                      value: "timestamp+incrementing",
                      label: "timestamp+incrementing — both, for rows sharing a timestamp",
                    },
                    { value: "bulk", label: "bulk — re-read everything each poll" },
                    { value: "marker", label: "marker — rows a flag says are unprocessed" },
                  ]}
                  onChange={(e) => setProperty("ReceiveMode", e.target.value)}
                />
              </Field>

              {mode === "" && !connectionMode && !chosenDetail.isLoading && (
                <p className="text-[12px] text-warn-700">
                  Nothing here or on the connection says how to find new rows, so this receiver
                  will fail on its first poll. Pick a mode.
                </p>
              )}

              {needsMark && (
                <Field
                  label="Mark processed"
                  hint="Run against each row once Bitween has accepted it: set a flag, move the row, delete it. Bind the row's key as the key parameter."
                >
                  <Select
                    value={properties.MarkProcessedStatement ?? ""}
                    disabled={disabled || statements.isLoading}
                    options={[
                      { value: "", label: "None — the connection's own, if it has one" },
                      ...available.map((s) => ({ value: s.name, label: s.name })),
                    ]}
                    onChange={(e) => setProperty("MarkProcessedStatement", e.target.value)}
                  />
                </Field>
              )}

              {needsMark && !properties.MarkProcessedStatement && (
                <p className="text-[12px] text-warn-700">
                  {properties.ReceiveMode} has no cursor, so without a mark-processed statement
                  every poll reads the same rows again, forever.
                </p>
              )}

              <Field
                label="Batch size"
                hint="Rows one poll may take. The next poll takes the next batch. Blank uses the connection's default."
              >
                <TextInput
                  type="number"
                  min={1}
                  value={properties.ReceiveBatchSize ?? ""}
                  disabled={disabled}
                  onChange={(e) => setProperty("ReceiveBatchSize", e.target.value)}
                />
              </Field>

              {chosenStatement && (
                <StatementColumns statement={chosenStatement} needsCursor={needsCursor} mode={mode} />
              )}
            </>
          )}

          <p className="text-[12px] text-ink-500">
            <Link className="text-accent-600 hover:underline" to={`/data-sources/${chosen.id}`}>
              Manage {chosen.name}&apos;s statements
            </Link>
          </p>
        </>
      )}
    </div>
  );
}

/**
 * Where a delivery publishes on the customer's own broker.
 *
 * This is the other half of an external bus connection, and until now it was the missing half:
 * the adapters could publish and nothing in the pipeline ever asked them to, so an integration
 * could consume from a customer's queue and had no way to answer on one.
 *
 * Only three things are asked for, and the rest is deliberately left to the broker. Exchange and
 * routing key are RabbitMQ's vocabulary and are simply ignored by a queue-only broker such as SQS
 * — shown rather than hidden behind provider sniffing, because a form that quietly changes shape
 * is harder to explain than two fields that do not apply.
 *
 * The endpoint is NOT restricted to the queues this connection consumes. The common case for
 * egress is a queue Bitween does not drain, and constraining it would rule that out.
 */
function BrokerDelivery({
  connection,
  properties,
  disabled,
  setProperty,
}: {
  connection: string;
  properties: Record<string, string>;
  disabled?: boolean;
  setProperty: (name: string, value: string) => void;
}) {
  const endpoint = properties.Endpoint ?? "";
  const exchange = properties.Exchange ?? "";

  return (
    <>
      <Field
        label="Publish to"
        hint={`A queue on ${connection}. For SQS this is the full queue URL; for RabbitMQ, the queue name. It does not have to be a queue this connection consumes — usually it is not.`}
      >
        <TextInput
          value={endpoint}
          disabled={disabled}
          spellCheck={false}
          placeholder="orders.outbound"
          onChange={(e) => setProperty("Endpoint", e.target.value)}
        />
      </Field>

      <Field
        label="Exchange"
        hint="RabbitMQ only, and optional: publish through an exchange rather than straight to a queue. Ignored by a broker that has no exchanges."
      >
        <TextInput
          value={exchange}
          disabled={disabled}
          spellCheck={false}
          onChange={(e) => setProperty("Exchange", e.target.value)}
        />
      </Field>

      {exchange && (
        <Field label="Routing key" hint="Which binding the exchange should match.">
          <TextInput
            value={properties.RoutingKey ?? ""}
            disabled={disabled}
            spellCheck={false}
            onChange={(e) => setProperty("RoutingKey", e.target.value)}
          />
        </Field>
      )}

      {!endpoint && !exchange && (
        <p className="text-[12px] text-warn-700">
          Nothing to publish to, so this delivery will fail on its first message. Name a queue, or
          an exchange.
        </p>
      )}

      {/* The thing worth knowing about a broker connection and several nodes, said where the
          decision is made rather than in a document nobody reads at the time. */}
      <p className="text-[12px] text-ink-500">
        A broker connection is held by one node so its queues are drained once, but a delivery runs
        wherever the message was picked up — so publishing opens a short send-only connection on
        that node. It never subscribes, so nothing is consumed twice.
      </p>
    </>
  );
}

/**
 * The polled statement's key and cursor columns, edited from the subscription that polls with it.
 *
 * They belong to the STATEMENT, not to this subscription, and that is not an accident: the SQL
 * already decided them. A statement reading `where id > @cursor order by id` has `id` as its
 * cursor whoever polls it — a subscription nominating anything else would have the adapter save a
 * value of one type and bind it into a comparison against another. The key column is welded the
 * same way, to the mark-processed statement that binds it as `@key`.
 *
 * But storing them there does not mean making someone go there. The version of this that only
 * warned — "set its key column on the connection" — sent the reader to a second screen to finish
 * a job they had started here. So the fields are here, and the write goes there.
 *
 * Which makes this the one control on this panel that does NOT edit the subscription: it saves
 * immediately, to a record other subscriptions share. Both facts are said out loud, and the save
 * is a button rather than a blur, so it is never something that happened while you were looking
 * somewhere else.
 */
function StatementColumns({
  statement,
  needsCursor,
  mode,
}: {
  statement: DataSourceStatement;
  needsCursor: boolean;
  mode: string;
}) {
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("data-source-statements.edit");

  const [keyColumn, setKeyColumn] = useState(statement.keyColumn ?? "");
  const [cursorColumn, setCursorColumn] = useState(statement.cursorColumn ?? "");
  const [error, setError] = useState<string | null>(null);

  // Saving here leaves the fields exactly as they were — the values are already on screen, which
  // is precisely why it needs saying. Two seconds in place of the button, the same way the
  // mapping editor says it.
  const [justSaved, setJustSaved] = useState(false);

  // Choosing a different statement means different columns; without this the boxes would keep the
  // previous statement's and offer to write them onto this one.
  useEffect(() => {
    setKeyColumn(statement.keyColumn ?? "");
    setCursorColumn(statement.cursorColumn ?? "");
    setError(null);
  }, [statement.id, statement.keyColumn, statement.cursorColumn]);

  // True when the database never looked at the SQL — see the statements panel, which says the
  // same thing for the same reason.
  const [unchecked, setUnchecked] = useState(false);

  const save = useMutation({
    mutationFn: () =>
      api.updateDataSourceStatement(statement.id, {
        name: statement.name,
        sql: statement.sql,
        description: statement.description,
        workGroupId: statement.workGroupId,
        inactive: statement.inactive,
        keyColumn,
        cursorColumn,
      }),
    onSuccess: (saved) => {
      setError(null);
      setUnchecked(!saved.checked);
      setJustSaved(true);
      setTimeout(() => setJustSaved(false), 2000);
      void queryClient.invalidateQueries({
        queryKey: keys.dataSourceStatements.forDataSource(statement.dataSourceId),
      });
    },
    onError: (e: Error) => setError(e.message),
  });

  const dirty =
    keyColumn !== (statement.keyColumn ?? "") || cursorColumn !== (statement.cursorColumn ?? "");

  return (
    <div className="space-y-3 rounded-lg border border-ink-200 bg-white p-3">
      <p className="text-[12px] text-ink-500">
        These describe the rows <span className="font-medium text-ink-700">{statement.name}</span>{" "}
        returns, so they live on the statement and are shared by every subscription polling it.
        Saving here changes it for all of them.
      </p>

      <div className="grid gap-3 sm:grid-cols-2">
        <Field
          label="Key column"
          hint="Which column identifies a row, for mark-processed and deduplication. Required for polling."
        >
          <TextInput
            value={keyColumn}
            disabled={!canEdit}
            placeholder="id"
            spellCheck={false}
            onChange={(e) => setKeyColumn(e.target.value)}
          />
        </Field>

        <Field
          label="Cursor column"
          hint="Which column the receiver follows. It has to be the column the statement compares against the cursor. Not needed for bulk or marker."
        >
          <TextInput
            value={cursorColumn}
            disabled={!canEdit}
            placeholder="id"
            spellCheck={false}
            onChange={(e) => setCursorColumn(e.target.value)}
          />
        </Field>
      </div>

      {error && <FormError>{error}</FormError>}

      {unchecked && !error && (
        <p className="text-[12px] text-warn-700">
          Saved, but not checked — this connection is not running here, so the database never saw
          the change.
        </p>
      )}

      {!canEdit && (
        <p className="text-[12px] text-ink-500">
          Changing these needs the right to edit this connection&apos;s statements.
        </p>
      )}

      {justSaved && (
        <span className="flex items-center gap-1 text-sm font-medium text-ok-600">
          <Check className="size-4" /> Saved to {statement.name}
        </span>
      )}

      {dirty && canEdit && !justSaved && (
        <div className="flex items-center gap-2">
          <Button size="sm" onClick={() => save.mutate()} busy={save.isPending}>
            Save to statement
          </Button>
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setKeyColumn(statement.keyColumn ?? "");
              setCursorColumn(statement.cursorColumn ?? "");
            }}
          >
            Discard
          </Button>
          <span className="text-[12px] text-ink-500">Saves now, not with the subscription.</span>
        </div>
      )}

      {/* The two ways this fails at poll time rather than here. Shown against what is SAVED, not
          what is typed, so a warning does not vanish the moment someone starts typing. */}
      {!statement.keyColumn && (
        <p className="text-[12px] text-warn-700">
          Without a key column this receiver fails on its first poll: a row cannot be identified,
          so it cannot be marked processed or deduplicated.
        </p>
      )}

      {needsCursor && !statement.cursorColumn && (
        <p className="text-[12px] text-warn-700">
          {mode} follows a column, and this statement does not say which of its columns that is.
        </p>
      )}

      {statement.cursorColumn && !/[:@]cursor\b/i.test(statement.sql) && (
        <p className="text-[12px] text-warn-700">
          The statement&apos;s SQL never mentions the cursor, so every poll would read from the
          beginning again.
        </p>
      )}
    </div>
  );
}
