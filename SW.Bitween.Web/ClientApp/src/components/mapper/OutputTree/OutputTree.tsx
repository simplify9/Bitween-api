import React from "react";
import type { TreeNode } from "../../../lib/mapping/mappingPreview";
import { buildTypeMap } from "../../../lib/mapping/scribanGenerator";
import { useMappingEditorState } from "../../../lib/mapping/MappingEditorContext";
import { OutputLeaf } from "./OutputLeaf";
import { OutputBranch } from "./OutputBranch";


interface OutputTreeProps {
  nodes: TreeNode[];
  sourcePaths: string[];
  onLeafRef?: (path: string, el: HTMLElement | null) => void;
}

const OutputTree: React.FC<OutputTreeProps> = ({ nodes, sourcePaths, onLeafRef }) => {
  const { searchOutput: search, outputJson } = useMappingEditorState();
  const typeMap = React.useMemo(() => { try { return buildTypeMap(outputJson); } catch { return {}; } }, [outputJson]);
  if (nodes.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-full text-center px-4 gap-3">
        <div className="text-3xl mb-1 text-ink-300">{'{}'}</div>
        <p className="text-xs text-ink-400">No output fields. Paste a target JSON sample above to populate the tree.</p>
      </div>
    );
  }

  return (
    <div className="px-2 py-1 space-y-0.5">
      {nodes.map((node) =>
        node.type === 'leaf' ? (
          <OutputLeaf
            key={node.path}
            node={node}
            sourcePaths={sourcePaths}
            typeMap={typeMap}
            onLeafRef={onLeafRef}
            isSearchMatch={Boolean(search && node.key.toLowerCase().includes(search.toLowerCase()))}
          />
        ) : (
          <OutputBranch key={node.path} node={node} sourcePaths={sourcePaths} typeMap={typeMap} onLeafRef={onLeafRef} search={search} />
        )
      )}
    </div>
  );
};

export default OutputTree;
