import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { Can } from "../../auth/guards";
import { Panel } from "../ui/Panel";
import { TrailTable } from "./shared";

/**
 * What has happened to one row.
 *
 * Self-fetching on purpose: the alternative - folding the trail into each entity's detail
 * request, as this originally did for information types and subscriptions - means adding
 * history to a page costs a change to that page's API client, its detail type and its handler,
 * and makes the whole page wait on a query nobody is looking at yet. Here it is one line per
 * page, and the page renders without it.
 */
export function HistoryList({
  entityName,
  entityKey,
}: {
  /** The audited entity's name, as the backend records it - e.g. `Partner`. */
  entityName: string;
  /** Its primary key. Omit to show every change to entities of this kind. */
  entityKey?: string | number;
}) {
  const key = entityKey === undefined ? undefined : String(entityKey);

  const { data, isLoading } = useQuery({
    queryKey: keys.audit.entity(entityName, key ?? null),
    queryFn: () => api.searchAudit({ entityName, entityKey: key, offset: 0, limit: 8 }),
  });

  if (isLoading) return <p className="text-sm text-ink-500">Loading…</p>;
  return <TrailTable entries={data?.result ?? []} />;
}

/**
 * `HistoryList` in a panel of its own, for the entity pages. Wrapped in `Can` so it
 * simply isn't there for a session without `audit.view` - hide, don't dim.
 *
 * Hosts that bring their own section chrome (the member drawer) use `HistoryList` directly.
 */
export function HistoryCard({
  entityName,
  entityKey,
  description,
  className = "",
}: {
  entityName: string;
  entityKey?: string | number;
  description?: string;
  className?: string;
}) {
  return (
    <Can permission="audit.view">
      <Panel title="History" description={description} className={className}>
        <HistoryList entityName={entityName} entityKey={entityKey} />
      </Panel>
    </Can>
  );
}
