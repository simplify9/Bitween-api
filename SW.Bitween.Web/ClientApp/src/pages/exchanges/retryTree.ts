import { api, type RetryTree, type RetryTreeNode } from "../../api";
import { keys } from "../../api/queryKeys";

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
