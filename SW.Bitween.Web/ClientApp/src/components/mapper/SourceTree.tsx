import type React from "react";
import { useCallback } from "react";
import { flattenLeafPaths } from "../../lib/mapping/mappingPreview";
import type { TreeNode } from "../../lib/mapping/mappingPreview";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  addFieldMapping,
  setHoveredPath,
  toggleNodeCollapsed,
} from "../../lib/mapping/MappingEditorContext";

// ─── Types ────────────────────────────────────────────────────────────────────

interface SourceTreeProps {
  nodes: TreeNode[];
  /** Called when a leaf is clicked or dragged onto an awaiting target */
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
}

interface LeafProps {
  node: TreeNode;
  isAssigned: boolean;
  isHovered: boolean;
  isSearchMatch: boolean;
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
}

interface BranchProps {
  node: TreeNode;
  depth?: number;
  assignedPaths: Set<string>;
  search: string;
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
}

// ─── SourceLeaf ───────────────────────────────────────────────────────────────

const SourceLeaf: React.FC<LeafProps> = ({ node, isAssigned, isHovered, isSearchMatch, onLeafRef }) => {
  const dispatch = useMappingEditorDispatch();
  const { selectedMappingId: selectedId } = useMappingEditorState();

  const ref = useCallback(
    (el: HTMLElement | null) => {
      onLeafRef?.(node.path, el);
    },
    [node.path, onLeafRef]
  );

  const handleDragStart = (e: React.DragEvent) => {
    e.dataTransfer.setData('text/plain', node.path);
    e.dataTransfer.effectAllowed = 'link';
  };

  const handleClick = (e: React.MouseEvent) => {
    e.stopPropagation();
    // If a target field is selected, assign this source to it
    if (selectedId) {
      dispatch(
        addFieldMapping({
          source: node.path,
          target: '', // will be set by the output tree handler
        })
      );
    }
  };

  return (
    <div
      ref={ref}
      draggable
      onDragStart={handleDragStart}
      onMouseEnter={() => dispatch(setHoveredPath(node.path))}
      onMouseLeave={() => dispatch(setHoveredPath(null))}
      onClick={handleClick}
      title={`${node.path}${node.value !== undefined ? ` = ${JSON.stringify(node.value)}` : ''}`}
      className={[
        'flex items-center gap-1.5 px-2 py-[3px] rounded text-xs font-mono cursor-grab select-none transition-colors',
        isHovered ? 'bg-crimson-50 text-crimson-700' : '',
        isAssigned ? 'text-ok-600' : 'text-ink-700',
        isSearchMatch ? 'bg-warn-100 ring-1 ring-warn-100' : '',
        'hover:bg-crimson-50',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <span
        className={[
          'w-2 h-2 rounded-full flex-shrink-0',
          isAssigned ? 'bg-ok-100' : 'bg-ink-300',
        ].join(' ')}
      />
      <span className="truncate">{node.key}</span>
      {node.value !== undefined && (
        <span className="ml-auto text-ink-400 truncate max-w-[80px]">
          {String(node.value).substring(0, 20)}
        </span>
      )}
    </div>
  );
};

// ─── SourceBranch ─────────────────────────────────────────────────────────────

const SourceBranch: React.FC<BranchProps> = ({ node, depth = 0, assignedPaths, search, onLeafRef }) => {
  const dispatch = useMappingEditorDispatch();
  const { collapsedNodes } = useMappingEditorState();
  const collapsed = collapsedNodes.includes(node.path);
  const isOpen = !collapsed;

  const leaves = flattenLeafPaths(node);
  const assignedCount = leaves.filter((p) => assignedPaths.has(p)).length;

  return (
    <div>
      <button
        onClick={() => dispatch(toggleNodeCollapsed(node.path))}
        className="flex items-center gap-1 w-full text-left px-2 py-[3px] rounded hover:bg-ink-100 transition-colors group"
      >
        <span className="text-ink-400 text-xs w-3 flex-shrink-0">{isOpen ? '▾' : '▸'}</span>
        <span className="text-xs font-medium text-ink-700 font-mono truncate">{node.key}</span>
        {node.type === 'array' && (
          <span className="text-xs text-crimson-500 font-mono ml-0.5">[{node.itemCount ?? '*'}]</span>
        )}
        {node.type === 'object' && (
          <span className="text-xs text-ink-400 font-mono ml-0.5">{'{}'}</span>
        )}
        <span
          className={[
            'ml-auto text-xs',
            assignedCount === leaves.length && leaves.length > 0
              ? 'text-ok-600'
              : 'text-ink-400',
          ].join(' ')}
        >
          {assignedCount}/{leaves.length}
        </span>
      </button>
      {isOpen && (
        <div className={`pl-3 border-l border-ink-100 ${depth > 0 ? 'ml-3' : 'ml-3'}`}>
          {node.children.map((child) =>
            child.type === 'leaf' ? (
              <SourceLeaf
                key={child.path}
                node={child}
                isAssigned={assignedPaths.has(child.path)}
                isHovered={false}
                isSearchMatch={Boolean(search && child.key.toLowerCase().includes(search.toLowerCase()))}
                onLeafRef={onLeafRef}
              />
            ) : (
              <SourceBranch
                key={child.path}
                node={child}
                depth={depth + 1}
                assignedPaths={assignedPaths}
                search={search}
                onLeafRef={onLeafRef}
              />
            )
          )}
        </div>
      )}
    </div>
  );
};

// ─── SourceTree ───────────────────────────────────────────────────────────────

const SourceTree: React.FC<SourceTreeProps> = ({ nodes, onLeafRef }) => {
  const { searchInput: search, fieldMappings } = useMappingEditorState();
  const assignedPaths = new Set(fieldMappings.map((m) => m.source).filter(Boolean));

  if (nodes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full text-center px-4">
        <div className="text-3xl mb-2 text-ink-300">{'{ }'}</div>
        <p className="text-xs text-ink-400">Paste source JSON above to see fields</p>
      </div>
    );
  }

  return (
    <div className="px-2 py-1 space-y-0.5">
      {nodes.map((node) =>
        node.type === 'leaf' ? (
          <SourceLeaf
            key={node.path}
            node={node}
            isAssigned={assignedPaths.has(node.path)}
            isHovered={false}
            isSearchMatch={Boolean(search && node.key.toLowerCase().includes(search.toLowerCase()))}
            onLeafRef={onLeafRef}
          />
        ) : (
          <SourceBranch
            key={node.path}
            node={node}
            assignedPaths={assignedPaths}
            search={search}
            onLeafRef={onLeafRef}
          />
        )
      )}
    </div>
  );
};

export default SourceTree;
