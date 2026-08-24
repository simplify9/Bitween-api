import type React from "react";
import { useCallback } from "react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import type { FieldMapping } from "../../../lib/mapping/types";

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ArrayInnerFieldProps {
  node: TreeNode;
  innerMapping: FieldMapping | undefined;
  isMapped: boolean;
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
  isSearchMatch?: boolean;
  onOpenArrayModal: () => void;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function resolveDisplaySource(m: FieldMapping | undefined): string | null {
  if (!m) return null;
  if (m.source) return m.source;
  if (m.fixedValue !== undefined) return `"${m.fixedValue}"`;
  if (m.partnerPropKey) return `__partner__.${m.partnerPropKey}`;
  if (m.globalSetId && m.globalKey) return `__globals__.${m.globalSetId}["${m.globalKey}"]`;
  return null;
}

// ─── ArrayInnerField ──────────────────────────────────────────────────────────
// Read-only row for a field that lives inside an array loop.
// All editing is done via the array loop modal — clicking opens it.

export const ArrayInnerField: React.FC<ArrayInnerFieldProps> = ({
  node,
  innerMapping,
  isMapped,
  onLeafRef,
  isSearchMatch,
  onOpenArrayModal,
}) => {
  const ref = useCallback(
    (el: HTMLElement | null) => onLeafRef?.(node.path, el),
    [node.path, onLeafRef]
  );

  const displaySource = resolveDisplaySource(innerMapping);

  return (
    <div
      ref={ref}
      onClick={onOpenArrayModal}
      title="Managed by array mapping — click to open loop editor"
      className={[
        'flex items-center gap-1.5 px-2 py-[3px] rounded border border-transparent text-xs cursor-pointer transition-all select-none hover:border-crimson-200 hover:bg-crimson-50',
        isSearchMatch ? 'bg-warn-100 ring-1 ring-warn-100' : '',
      ].filter(Boolean).join(' ')}
    >
      <span className={['w-2 h-2 rounded-full flex-shrink-0', isMapped ? 'bg-ok-100' : 'bg-danger-300'].join(' ')} />
      <span className="font-mono text-ink-700 truncate">{node.key}</span>
      <span className="text-ink-300 mx-0.5 flex-shrink-0">←</span>
      <span className={['font-mono text-xs truncate flex-1', displaySource ? 'text-crimson-600' : 'text-ink-400'].join(' ')}>
        {displaySource ?? '— unassigned —'}
      </span>
      <span className="text-[10px] text-crimson-400 border border-crimson-200 rounded px-1 flex-shrink-0 bg-crimson-50">
        loop
      </span>
    </div>
  );
};
