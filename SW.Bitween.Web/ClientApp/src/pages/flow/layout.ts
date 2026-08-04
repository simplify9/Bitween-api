import type { FlowEdge, FlowGraph, FlowNode, FlowNodeKind } from "./model";

/**
 * Turns the graph into coordinates.
 *
 * Layered left to right by longest path, so distance from the left edge means
 * "how many hops from something that starts outside Bitween" — the one spatial
 * fact worth encoding. Nodes are never dragged: the layout *is* the configuration,
 * and letting it be rearranged would only produce diagrams that disagree with it.
 */

export const NODE_W = 216;
/** Uniform, so every edge can anchor at a known mid-height without measuring. */
export const NODE_H = 92;
const GAP_X = 92;
const GAP_Y = 22;
const PAD = 28;
/** Room under the last row for loop edges to bow through. */
const LOOP_ROOM = 72;

export interface PlacedNode extends FlowNode {
  x: number;
  y: number;
}

export interface PlacedEdge extends FlowEdge {
  path: string;
  /** Closes a cycle: messages published this way circulate without end. */
  loop: boolean;
}

export interface FlowLayout {
  nodes: PlacedNode[];
  edges: PlacedEdge[];
  width: number;
  height: number;
  /** Nodes that sit on a cycle, so their cards can say so too. */
  looping: Set<string>;
}

/** Column order within a layer before crossings are considered. */
const KIND_ORDER: Record<FlowNodeKind, number> = {
  message: 0,
  busGateway: 1,
  apiGateway: 2,
  job: 3,
  integration: 4,
};

/**
 * Edges that point back at something already on the stack.
 *
 * Depth-first, iteratively: a bus topology can genuinely cycle (A publishes what B
 * listens for, B publishes what A listens for) and a recursive walk over one would
 * be the second infinite loop in the same picture.
 */
const UNSEEN = 0;
const ON_STACK = 1;
const DONE = 2;

function findBackEdges(nodes: FlowNode[], outgoing: Map<string, FlowEdge[]>): Set<string> {
  const back = new Set<string>();
  const state = new Map<string, number>();

  for (const root of nodes) {
    if ((state.get(root.id) ?? UNSEEN) !== UNSEEN) continue;
    state.set(root.id, ON_STACK);
    const stack: { id: string; next: number }[] = [{ id: root.id, next: 0 }];
    while (stack.length > 0) {
      const top = stack[stack.length - 1];
      const out = outgoing.get(top.id) ?? [];
      if (top.next >= out.length) {
        state.set(top.id, DONE);
        stack.pop();
        continue;
      }
      const edge = out[top.next++];
      const seen = state.get(edge.to) ?? UNSEEN;
      if (seen === ON_STACK) back.add(edge.id);
      else if (seen === UNSEEN) {
        state.set(edge.to, ON_STACK);
        stack.push({ id: edge.to, next: 0 });
      }
    }
  }
  return back;
}

/** Append to a keyed bucket, creating it on first use. */
function bucket<T>(map: Map<string, T[]>, key: string, value: T): void {
  const list = map.get(key);
  if (list) list.push(value);
  else map.set(key, [value]);
}

export function layoutFlow({ nodes, edges }: FlowGraph): FlowLayout {
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const outgoing = new Map<string, FlowEdge[]>();
  for (const e of edges) {
    if (!byId.has(e.from) || !byId.has(e.to)) continue;
    bucket(outgoing, e.from, e);
  }

  const back = findBackEdges(nodes, outgoing);
  const forward = edges.filter((e) => !back.has(e.id) && byId.has(e.from) && byId.has(e.to));

  // Longest path over the remaining DAG. Dropping the back edges is what
  // guarantees this terminates.
  const depth = new Map(nodes.map((n) => [n.id, 0]));
  const remaining = new Map(nodes.map((n) => [n.id, 0]));
  for (const e of forward) remaining.set(e.to, remaining.get(e.to)! + 1);
  const ready = nodes.filter((n) => remaining.get(n.id) === 0).map((n) => n.id);
  const forwardOut = new Map<string, FlowEdge[]>();
  for (const e of forward) bucket(forwardOut, e.from, e);
  while (ready.length > 0) {
    const id = ready.shift()!;
    for (const e of forwardOut.get(id) ?? []) {
      depth.set(e.to, Math.max(depth.get(e.to)!, depth.get(id)! + 1));
      remaining.set(e.to, remaining.get(e.to)! - 1);
      if (remaining.get(e.to) === 0) ready.push(e.to);
    }
  }

  const columns: FlowNode[][] = [];
  for (const n of nodes) {
    const d = depth.get(n.id)!;
    (columns[d] ??= []).push(n);
  }
  for (const col of columns)
    col.sort((a, b) => KIND_ORDER[a.kind] - KIND_ORDER[b.kind] || a.title.localeCompare(b.title));

  // One barycentre pass: put each node next to the average row of what feeds it.
  // Cheap, and it takes the obvious crossings out of a graph this shape.
  const rowOf = new Map<string, number>();
  columns.forEach((col) => col.forEach((n, row) => rowOf.set(n.id, row)));
  const incoming = new Map<string, string[]>();
  for (const e of forward) bucket(incoming, e.to, e.from);
  for (let c = 1; c < columns.length; c++) {
    const col = columns[c];
    const anchor = new Map(
      col.map((n) => {
        const from = (incoming.get(n.id) ?? []).map((id) => rowOf.get(id) ?? 0);
        return [n.id, from.length > 0 ? from.reduce((a, b) => a + b, 0) / from.length : rowOf.get(n.id)!];
      }),
    );
    col.sort((a, b) => anchor.get(a.id)! - anchor.get(b.id)! || a.title.localeCompare(b.title));
    col.forEach((n, row) => rowOf.set(n.id, row));
  }

  const tallest = Math.max(1, ...columns.map((c) => c.length));
  const rowPitch = NODE_H + GAP_Y;
  const placed: PlacedNode[] = [];
  columns.forEach((col, c) => {
    // Centred against the tallest column so the whole diagram reads as one band
    // rather than a staircase hugging the top edge.
    const offset = ((tallest - col.length) * rowPitch) / 2;
    col.forEach((n, row) => {
      placed.push({ ...n, x: PAD + c * (NODE_W + GAP_X), y: PAD + offset + row * rowPitch });
    });
  });

  const pos = new Map(placed.map((n) => [n.id, n]));
  const bottom = PAD + tallest * rowPitch - GAP_Y;

  const drawn: PlacedEdge[] = [];
  for (const e of edges) {
    const a = pos.get(e.from);
    const b = pos.get(e.to);
    if (!a || !b) continue;
    const loop = back.has(e.id);
    drawn.push({ ...e, loop, path: loop ? loopPath(a, b, bottom) : forwardPath(a, b) });
  }

  const looping = new Set<string>();
  for (const e of drawn)
    if (e.loop) {
      looping.add(e.from);
      looping.add(e.to);
    }

  return {
    nodes: placed,
    edges: drawn,
    width: PAD * 2 + Math.max(NODE_W, columns.length * (NODE_W + GAP_X) - GAP_X),
    height: bottom + PAD + (looping.size > 0 ? LOOP_ROOM : 0),
    looping,
  };
}

/** Right edge to left edge, flattening out at both ends so arrows meet cards square. */
function forwardPath(a: PlacedNode, b: PlacedNode): string {
  const x1 = a.x + NODE_W;
  const y1 = a.y + NODE_H / 2;
  const x2 = b.x;
  const y2 = b.y + NODE_H / 2;
  const bend = Math.max(36, (x2 - x1) / 2);
  return `M ${x1} ${y1} C ${x1 + bend} ${y1}, ${x2 - bend} ${y2}, ${x2} ${y2}`;
}

/**
 * Under the diagram and back. Bowed below every card rather than threaded between
 * them, because the one thing a loop edge has to be is unmistakably a loop.
 */
function loopPath(a: PlacedNode, b: PlacedNode, bottom: number): string {
  const x1 = a.x + NODE_W / 2;
  const x2 = b.x + NODE_W / 2;
  const dip = bottom + LOOP_ROOM / 2;
  return `M ${x1} ${a.y + NODE_H} C ${x1} ${dip}, ${x2} ${dip}, ${x2} ${b.y + NODE_H}`;
}
