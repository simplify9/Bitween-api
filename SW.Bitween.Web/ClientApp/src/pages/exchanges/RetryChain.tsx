import { Link, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api, type RetryTree, type RetryTreeNode } from "../../api";
import { keys } from "../../api/queryKeys";
import { Badge } from "../../components/ui/basics";
import { PromotedProps, namesSomething } from "../../components/config/shared";
import { timeAgo, timeUntil } from "../../lib/dates";
import { StatusBadge } from "./shared";

/**
 * The whole chain is worth asking for only when the row says there is something in it. Both
 * facts come back with every exchange row, so an exchange that was never retried and is not
 * itself a retry — most of them — costs no request at all.
 */
export const hasRetryChain = (x: { retryFor: string | null; hasRetry: boolean }) =>
  x.retryFor !== null || x.hasRetry;

export const retryTreeQuery = (id: string) => ({
  queryKey: keys.exchanges.retryTree(id),
  queryFn: () => api.getRetryTree(id),
  /**
   * A chain is settled history above the exchange asked about and only ever grows below it, so
   * a held copy cannot be wrong about what it shows — at worst it is missing an attempt someone
   * has just started, which invalidating on retry covers.
   */
  staleTime: 60_000,
});

/** The end of the chain below `fromId` — the attempt a retry would actually run. */
export function newestAttempt(tree: RetryTree, fromId: string): RetryTreeNode | null {
  let current = tree.attempts.find((a) => a.id === fromId) ?? null;
  if (!current) return null;

  for (;;) {
    // Newest first, so a chain that forked before one-retry-per-exchange was enforced resolves
    // the same way the backend resolves it.
    const children = tree.attempts
      .filter((a) => a.retryFor === current!.id)
      .sort((a, b) => b.startedOn.localeCompare(a.startedOn));
    if (children.length === 0) return current;
    current = children[0];
  }
}

const childrenOf = (tree: RetryTree, id: string | null) =>
  tree.attempts
    .filter((a) => a.retryFor === id)
    .sort((a, b) => a.startedOn.localeCompare(b.startedOn));

function Attempt({
  node,
  tree,
  currentId,
  depth,
}: {
  node: RetryTreeNode;
  tree: RetryTree;
  currentId: string;
  depth: number;
}) {
  const isCurrent = node.id === currentId;
  const children = childrenOf(tree, node.id);
  const navigate = useNavigate();

  return (
    <>
      <li
        // The whole row opens the attempt, the way a row of the exchange list opens its own
        // drawer — the identity on the right can no longer be a link itself, since promoted
        // properties bring a panel of their own and a button cannot sit inside a link.
        onClick={isCurrent ? undefined : () => navigate(`/exchanges?ids=${encodeURIComponent(node.id)}`)}
        className={`flex flex-wrap items-center gap-x-2 gap-y-1 rounded-md px-2 py-1.5 ${
          isCurrent ? "bg-ink-100/70" : "cursor-pointer hover:bg-ink-50"
        }`}
        style={{ marginLeft: depth * 14 }}
      >
        {/* The attempt number carries the link, rather than the identity on the right: promoted
            properties can open a panel of their own, and a button inside a link is neither. */}
        {isCurrent ? (
          <span
            className="w-[68px] shrink-0 text-[11px] font-medium tracking-wide text-ink-500 uppercase"
            title={`The ${ordinal(depth + 1)} attempt at this work — the one you are looking at`}
          >
            Attempt {depth + 1}
          </span>
        ) : (
          <Link
            to={`/exchanges?ids=${encodeURIComponent(node.id)}`}
            title={`Open the ${ordinal(depth + 1)} attempt at this work`}
            className="w-[68px] shrink-0 text-[11px] font-medium tracking-wide text-ink-500 uppercase hover:text-crimson-700 hover:underline"
          >
            Attempt {depth + 1}
          </Link>
        )}
        <StatusBadge status={node.status} />
        <Badge
          tone="neutral"
          title={
            node.retryFor === null
              ? "The original run — nobody retried anything to produce this"
              : node.manualRetry
                ? "Someone pressed Retry for this attempt"
                : "A retry policy scheduled this attempt"
          }
        >
          {node.retryFor === null ? "Original" : node.manualRetry ? "By hand" : "Auto"}
        </Badge>
        {/* Without this, a fork reads as a mistake: two branches put two different exchanges at
            the same depth, so the same attempt number appears twice and looks like one exchange
            retried twice over. Only reachable in exchanges retried before the rule existed. */}
        {children.length > 1 && (
          <Badge
            tone="warn"
            title="This exchange was retried more than once, which happened before an exchange was limited to a single retry. Each of those retries begins its own branch below — which is why an attempt number can appear twice."
          >
            {children.length} retries from here
          </Badge>
        )}
        {node.scheduledRetryOn && (
          <Badge tone="warn" title={`An auto-retry is scheduled for this attempt`}>
            Auto-retry {timeUntil(node.scheduledRetryOn)}
          </Badge>
        )}
        <span className="text-[13px] text-ink-500" title={node.startedOn}>
          {timeAgo(node.startedOn)}
        </span>

        {/* Named the way the exchange list names a row — the promoted properties are what
            someone recognises an exchange by, and the id falls back in when there are none. */}
        <span className="ml-auto flex items-center gap-2">
          {namesSomething(node.promotedProperties) ? (
            <PromotedProps properties={node.promotedProperties} />
          ) : (
            <span className="font-mono text-xs text-ink-500">{node.id}</span>
          )}
          {isCurrent && (
            <span className="shrink-0 text-[11px] font-medium tracking-wide text-ink-500 uppercase">
              You are here
            </span>
          )}
        </span>
      </li>
      {children.map((child) => (
        <Attempt key={child.id} node={child} tree={tree} currentId={currentId} depth={depth + 1} />
      ))}
    </>
  );
}

const ordinal = (n: number) => {
  const names = ["first", "second", "third", "fourth", "fifth"];
  return names[n - 1] ?? `${n}th`;
};

/**
 * Every attempt made at one piece of work, in order, with the exchange being looked at marked.
 * An exchange is retried at most once, so this reads as a chain; exchanges retried before that
 * rule was enforced can fork, and those show as branches rather than being hidden.
 */
export function RetryChain({ id }: { id: string }) {
  const { data: tree, isLoading } = useQuery(retryTreeQuery(id));

  // A line rather than a spinner block: this sits inside an already-rendered drawer, and it is
  // usually filled in before anyone looks at it — the row is prefetched on hover.
  if (isLoading)
    return <p className="text-[13px] text-ink-500">Loading the retry chain…</p>;
  if (!tree || tree.attempts.length < 2) return null;

  const root = tree.attempts.find((a) => a.id === tree.rootId) ?? tree.attempts[0];

  return (
    <section className="rounded-lg border border-ink-200 bg-white p-3">
      <h4
        className="mb-1.5 text-[11px] font-medium tracking-wide text-ink-500 uppercase"
        title="Every attempt at this work. An exchange is retried at most once, so retrying continues from the newest attempt rather than starting again from an old one."
      >
        Retry chain · {tree.attempts.length} attempts
      </h4>
      <ul className="space-y-0.5">
        <Attempt node={root} tree={tree} currentId={id} depth={0} />
      </ul>
      {tree.truncated && (
        <p className="mt-1.5 text-[13px] text-ink-500">
          Only the attempts nearest this one are shown — the chain is longer than this view walks.
        </p>
      )}
    </section>
  );
}
