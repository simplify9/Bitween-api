import type { ReactNode } from "react";
import type { UseQueryResult } from "@tanstack/react-query";
import { EmptyState, LoadError, LoadingBlock } from "./basics";

/**
 * The part of a list page under its toolbar: loading, a failure with a way to retry, an empty
 * list — told apart from a search that found nothing — or the rows.
 *
 * Every list page wrote this four-way choice out itself. A new one gets it right by using this.
 */
export function ListBody<T>({
  query,
  rows,
  what,
  filtered,
  empty,
  children,
}: {
  query: Pick<UseQueryResult<unknown>, "isPending" | "isError" | "error" | "refetch">;
  rows: T[];
  /** "retry policies": "Loading retry policies…", "Couldn't load retry policies". */
  what: string;
  /** A search or filter is narrowing the list, so empty means "nothing matches", not "nothing yet". */
  filtered: boolean;
  /** The list with nothing in it at all: what to say, and what to do about it. */
  empty: { icon: ReactNode; title: string; body: ReactNode; action?: ReactNode };
  children: (rows: T[]) => ReactNode;
}) {
  if (query.isPending) return <LoadingBlock label={`Loading ${what}…`} />;
  if (query.isError) return <LoadError error={query.error} what={what} onRetry={() => void query.refetch()} />;
  if (rows.length === 0)
    return filtered ? (
      <EmptyState icon={empty.icon} title="Nothing matches">
        Try a different search, or clear the filters.
      </EmptyState>
    ) : (
      <EmptyState icon={empty.icon} title={empty.title} action={empty.action}>
        {empty.body}
      </EmptyState>
    );
  return <>{children(rows)}</>;
}
