import type { MatchGroup, MatchNode } from "../api";

/**
 * Human-readable summary of a message filter, e.g.
 * `Store is CR-114 and Status is one of SHIPPED, DELIVERED`.
 * null (or an empty group) means the filter matches everything.
 */
export const matchSummary = (expr: MatchGroup | null): string => {
  if (!expr || expr.children.length === 0) return "All messages";
  return nodeSummary(expr, true);
};

const nodeSummary = (node: MatchNode, topLevel = false): string => {
  if ("path" in node) {
    const verb =
      node.values.length > 1 ? (node.op === "oneOf" ? "is one of" : "is none of") : node.op === "oneOf" ? "is" : "is not";
    return `${node.path} ${verb} ${node.values.join(", ")}`;
  }
  const joined = node.children.map((c) => nodeSummary(c)).join(node.op === "and" ? " and " : " or ");
  return topLevel || node.children.length < 2 ? joined : `(${joined})`;
};

/** An empty starting group for filter editors. */
export const emptyMatchGroup = (): MatchGroup => ({ op: "and", children: [] });
