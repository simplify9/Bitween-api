import type { MatchGroup, MatchNode } from "../types";

/**
 * The backend's match tree is strictly binary (and/or each take exactly two
 * operands) and uses snake_case type discriminators, unlike the frontend's
 * n-ary MatchGroup. Shared by Subscriptions and BusGatewayRoute — both use the
 * exact same backend type (`IPropertyMatchSpecification`).
 */
export type RawMatchSpec =
  | { type: "one_of" | "not_one_of"; path: string; values: string[] | null }
  | { type: "and" | "or"; left: RawMatchSpec; right: RawMatchSpec };

/**
 * Fold the backend's binary and/or tree into the frontend's n-ary MatchGroup,
 * flattening runs of the same operator so a flat group round-trips back flat
 * instead of as a deeply right-nested tree.
 */
function toMatchNode(spec: RawMatchSpec): MatchNode {
  if (!("left" in spec)) {
    return { op: spec.type === "one_of" ? "oneOf" : "notOneOf", path: spec.path, values: spec.values ?? [] };
  }
  const op = spec.type;
  const children: MatchNode[] = [];
  const collect = (s: RawMatchSpec) => {
    if (s.type === op) {
      collect(s.left);
      collect(s.right);
    } else {
      children.push(toMatchNode(s));
    }
  };
  collect(spec);
  return { op, children };
}

export const toMatchGroup = (spec: RawMatchSpec | null): MatchGroup | null => {
  if (!spec) return null;
  const node = toMatchNode(spec);
  // The backend can't represent a single-condition group (and/or always need
  // two operands), so a lone condition arrives unwrapped and must be rewrapped
  // here to satisfy the "root is always a group" contract.
  return "children" in node ? node : { op: "and", children: [node] };
};

/** Unfold an n-ary MatchGroup into the backend's binary tree, right-associatively. */
function toBackendNode(node: MatchNode): RawMatchSpec | null {
  if ("path" in node) {
    return { type: node.op === "oneOf" ? "one_of" : "not_one_of", path: node.path, values: node.values };
  }
  const parts = node.children.map(toBackendNode).filter((s): s is RawMatchSpec => s !== null);
  if (parts.length === 0) return null; // empty group — matches everything, i.e. no constraint
  if (parts.length === 1) return parts[0];
  return parts.reduceRight((right, left) => ({ type: node.op, left, right }));
}

export const toRawMatchExpression = (group: MatchGroup | null): RawMatchSpec | null =>
  group ? toBackendNode(group) : null;
