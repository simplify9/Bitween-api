import { api } from "../../api";

/**
 * The database catalog, as the adapter reports it.
 *
 * `Inspect` returns the adapter's own JSON as *text* - deliberately, because Bitween does not
 * model any provider's topology and a broker must be free to describe its own. That is right for
 * the raw panel and wrong for a browser, which has to group, count and drill into the answer. So
 * the parsing lives here, in one place, and everything above it works with real objects.
 *
 * Anything that does not parse is reported as such rather than thrown away: an adapter that
 * answered with something unexpected is a thing an operator needs to see, not a blank panel.
 */

export interface DbColumn {
  name: string;
  /** The engine's own name for it: NUMBER(10,2), VARCHAR2(50), TIMESTAMP WITH TIME ZONE. */
  dbType: string;
  /** What it arrives as in a result row, so a mapper author knows what to expect. */
  clrType: string;
  nullable: boolean;
  primaryKey: boolean;
  /** Identity, sequence-defaulted, GENERATED ALWAYS - anything the database fills in. */
  generated: boolean;
  ordinal: number;
}

export interface DbRoutineParameter {
  name: string;
  dbType: string;
  /** In | Out | InOut | ReturnValue | RefCursor. */
  direction: string;
  ordinal: number;
}

export interface DbObject {
  schema: string;
  name: string;
  /** table, view, materialized_view, procedure, function, sequence. */
  type: string;
  /** An estimate from the catalog, and null unless asked for. Never a COUNT(*). */
  rowCount: number | null;
  columns: DbColumn[];
  parameters: DbRoutineParameter[];
  comment: string | null;
}

export interface SchemaPage {
  objects: DbObject[];
  /** The page was full, so there is at least one more. */
  hasMore: boolean;
  /** What the adapter actually interpreted the request as, defaults filled in. */
  applied: Record<string, string>;
}

/** What the engine and this login can do. Only the parts the browser needs are named. */
export interface DbCapabilities {
  engine: string;
  serverVersion: string;
  /** Which object types are worth offering as tabs - the engine decides, not the UI. */
  supportedObjects: string[];
  schemaDiscovery: boolean;
  rowCountEstimates: boolean;
  /** `:` or `@`. The adapter reports its own, so a draft cannot use the wrong one. */
  parameterPrefix: string;
  /** How this engine limits rows: `limit`, `fetchFirst` or `top`. They are not interchangeable. */
  limitStyle: string;
}

/**
 * Everything a generated statement needs to know about the engine it is for.
 *
 * The browser used to write `@name` and `limit 100` whatever the connection was, which is right
 * for PostgreSQL and MySQL and wrong for the other two: Oracle binds `:name` and takes
 * `fetch first N rows only`, and SQL Server puts `top N` before the column list. A draft that
 * does not parse is worse than no draft, because it looks like something that was checked.
 */
export interface SqlDialect {
  parameterPrefix: string;
  limitStyle: string;
}

/** PostgreSQL's, and the safe assumption while the capability list is still loading. */
export const DEFAULT_DIALECT: SqlDialect = { parameterPrefix: "@", limitStyle: "limit" };

export const dialectOf = (capabilities: DbCapabilities | undefined): SqlDialect => ({
  parameterPrefix: capabilities?.parameterPrefix || DEFAULT_DIALECT.parameterPrefix,
  limitStyle: capabilities?.limitStyle || DEFAULT_DIALECT.limitStyle,
});

/**
 * Thrown when the adapter answered but not with what was expected. Separate from a transport
 * failure because the remedy is different: this one is a bug or a version mismatch, not a
 * connection to retry.
 */
export class UnreadableAnswerError extends Error {
  constructor(command: string, cause: unknown) {
    super(
      `The adapter answered ${command}, but the answer could not be read` +
        (cause instanceof Error ? `: ${cause.message}` : "."),
    );
    this.name = "UnreadableAnswerError";
  }
}

const parse = <T>(command: string, text: string | null): T => {
  try {
    return JSON.parse(text ?? "") as T;
  } catch (cause) {
    throw new UnreadableAnswerError(command, cause);
  }
};

/**
 * `ran: false` is not an exception on the wire - it is the ordinary answer from a node that does
 * not hold the connection - but for a caller it is indistinguishable from a failure, and the
 * adapter's own sentence explains it better than anything this layer could invent.
 */
const ranOrThrow = (result: { ran: boolean; result: string | null; error: string | null }) => {
  if (!result.ran) throw new Error(result.error ?? "The adapter did not answer.");
  return result.result;
};

export async function fetchCapabilities(dataSourceId: number): Promise<DbCapabilities> {
  const answer = await api.inspectDataSource(dataSourceId, "Describe");
  return parse<DbCapabilities>("Describe", ranOrThrow(answer));
}

export interface SchemaQuery {
  objectType: string;
  /** Empty means every schema this login can see. */
  schema?: string;
  /** Case-insensitive contains, matched by the database. Not a LIKE pattern. */
  nameLike?: string;
  skip?: number;
  take?: number;
  includeColumns?: boolean;
  includeRowCounts?: boolean;
}

export async function fetchSchemaPage(
  dataSourceId: number,
  query: SchemaQuery,
): Promise<SchemaPage> {
  // Strings the whole way: the argument map crosses two serialization boundaries before an
  // adapter binds it to a typed request, and "200"/"true" coerce cleanly at the far end.
  const args: Record<string, string> = { objectType: query.objectType };
  if (query.schema) args.schema = query.schema;
  if (query.nameLike) args.nameLike = query.nameLike;
  if (query.skip) args.skip = String(query.skip);
  if (query.take) args.take = String(query.take);
  if (query.includeColumns) args.includeColumns = "true";
  if (query.includeRowCounts) args.includeRowCounts = "true";

  const answer = await api.inspectDataSource(dataSourceId, "Discover", args);
  const page = parse<SchemaPage>("Discover", ranOrThrow(answer));
  return {
    objects: page.objects ?? [],
    hasMore: page.hasMore ?? false,
    applied: page.applied ?? {},
  };
}

/**
 * One object's detail. Asked for by exact name, because columns for every table at once is not a
 * menu - it is a download, and the adapter refuses to make it the default for that reason.
 *
 * `nameLike` is a contains match, so the answer can hold near-misses (`order` also finds
 * `order_line`); the exact name is picked out here rather than trusting the first row.
 */
export async function fetchSchemaObject(
  dataSourceId: number,
  objectType: string,
  schema: string,
  name: string,
): Promise<DbObject | null> {
  const page = await fetchSchemaPage(dataSourceId, {
    objectType,
    schema,
    nameLike: name,
    includeColumns: true,
    take: 50,
  });
  return (
    page.objects.find(
      (o) => o.name.toLowerCase() === name.toLowerCase() && o.schema === schema,
    ) ?? null
  );
}

/** The schemas present in a page, in the order they first appear. */
export const schemasOf = (objects: DbObject[]): string[] => [
  ...new Set(objects.map((o) => o.schema)),
];

/** Grouped for display, keeping the adapter's ordering inside each group. */
export const groupBySchema = (objects: DbObject[]): { schema: string; objects: DbObject[] }[] =>
  schemasOf(objects).map((schema) => ({
    schema,
    objects: objects.filter((o) => o.schema === schema),
  }));

/** The directions a caller provides a value for. Matched lower-case: the adapter sends `InOut`. */
const SUPPLIED = new Set(["in", "inout"]);

const QUOTED = /^[a-z_][a-z0-9_]*$/;

/**
 * How to write this object's name in SQL.
 *
 * Anything that is not a plain lowercase identifier is quoted, because an unquoted mixed-case or
 * hyphenated name resolves to something else or to nothing - and a browser that generates SQL a
 * user cannot run is worse than one that generates none.
 */
export const qualify = (schema: string, name: string): string =>
  [schema, name].map((part) => (QUOTED.test(part) ? part : `"${part}"`)).join(".");

/**
 * A starting statement for an object - the point of the browser, rather than a nicer way to read
 * the catalog.
 *
 * It is a starting point and not a finished query: no WHERE, because what to filter on is the one
 * thing the catalog cannot tell us, and a row limit, because the first thing anyone does with a
 * new statement is run it against a table whose size they do not know.
 */
export const draftStatementFor = (
  object: DbObject,
  dialect: SqlDialect = DEFAULT_DIALECT,
): string => {
  const target = qualify(object.schema, object.name);
  const bind = (name: string) => `${dialect.parameterPrefix}${name}`;

  if (object.type === "procedure" || object.type === "function") {
    // Only what the caller supplies. An out parameter, a return value and a REF CURSOR are the
    // routine's answer - binding them as inputs is how a generated call fails on first run.
    const args = object.parameters
      .filter((p) => SUPPLIED.has(p.direction?.toLowerCase()))
      .map((p) => bind(p.name))
      .join(", ");

    // A procedure is named, not written as SQL: that is what CommandType.StoredProcedure takes,
    // and on Oracle it is the only form that works. The parameters are bound by the message.
    return object.type === "function"
      ? `select * from ${target}(${args})`
      : target;
  }

  if (object.type === "sequence")
    return dialect.limitStyle === "limit"
      ? `select nextval('${object.schema}.${object.name}')`
      : dialect.limitStyle === "top"
        ? `select next value for ${target}`
        : `select ${target}.nextval from dual`;

  const columns = object.columns.length
    ? [...object.columns].sort((a, b) => a.ordinal - b.ordinal).map((c) => c.name).join(", ")
    : "*";

  // Three shapes, and they are not interchangeable - TOP goes before the columns, the other two
  // after the query.
  if (dialect.limitStyle === "top")
    return `select top 100 ${columns}\n  from ${target}`;

  const tail = dialect.limitStyle === "fetchFirst" ? "fetch first 100 rows only" : "limit 100";
  return `select ${columns}\n  from ${target}\n ${tail}`;
};

/** A name for the statement, derived from the object so two objects never collide. */
export const draftNameFor = (object: DbObject): string => {
  const verb = object.type === "procedure" ? "call" : "read";
  return `${verb}${object.name.replace(/(^|_)([a-z])/g, (_, __, c: string) => c.toUpperCase())}`;
};
