import type React from "react";
import { useEffect, useRef, useState, useCallback } from "react";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  removeFieldMapping,
  selectMapping,
} from "../../lib/mapping/MappingEditorContext";
import type { ArrayMapping } from "../../lib/mapping/types";

// ─── Path helpers (mirrors OutputTree / ArrayMappingModal) ────────────────────

function getFullSourcePath(amId: string, allMappings: ArrayMapping[]): string {
  const am = allMappings.find((m) => m.id === amId);
  if (!am) return '';
  if (!am.parentArrayId) return am.source;
  return `${getFullSourcePath(am.parentArrayId, allMappings)}[*].${am.source}`;
}

function getFullTargetPrefix(amId: string, allMappings: ArrayMapping[]): string {
  const am = allMappings.find((m) => m.id === amId);
  if (!am) return '';
  if (!am.parentArrayId) return am.target;
  return `${getFullTargetPrefix(am.parentArrayId, allMappings)}[*].${am.target}`;
}

interface Props {
  /** Map of path → DOM element for source leaves */
  sourceRefs: Map<string, HTMLElement>;
  /** Map of path → DOM element for target leaves */
  targetRefs: Map<string, HTMLElement>;
  /** The scrollable container for the source panel */
  sourceScroll: HTMLElement | null;
  /** The scrollable container for the target panel */
  targetScroll: HTMLElement | null;
  /** Width of the center canvas */
  width: number;
}

interface ConnectionLine {
  id: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  isSelected: boolean;
  isHovered: boolean;
  isArrayConnection?: boolean;
}

const CONNECTION_COLORS = {
  default: '#94a3b8',   // slate-400
  selected: '#3b82f6',  // blue-500
  hovered: '#10b981',   // emerald-500
  array: '#8b5cf6',     // violet-500 — for array loop connections
};

const ConnectionCanvas: React.FC<Props> = ({
  sourceRefs,
  targetRefs,
  sourceScroll,
  targetScroll,
}) => {
  const dispatch = useMappingEditorDispatch();
  const { fieldMappings, arrayMappings, selectedMappingId: selectedId, hoveredPath } = useMappingEditorState();
  const [lines, setLines] = useState<ConnectionLine[]>([]);
  const [hoveredLineId, setHoveredLineId] = useState<string | null>(null);
  const svgRef = useRef<SVGSVGElement>(null);
  const rafRef = useRef<number>(0);

  const computeLines = useCallback(() => {
    if (!svgRef.current) return;
    const svgRect = svgRef.current.getBoundingClientRect();
    const newLines: ConnectionLine[] = [];

    const addLine = (
      id: string,
      srcEl: HTMLElement,
      tgtEl: HTMLElement,
      isSelected: boolean,
      isHovered: boolean,
      isArrayConnection = false
    ) => {
      const srcRect = srcEl.getBoundingClientRect();
      const tgtRect = tgtEl.getBoundingClientRect();
      if (
        srcRect.bottom < svgRect.top - 20 ||
        srcRect.top > svgRect.bottom + 20 ||
        tgtRect.bottom < svgRect.top - 20 ||
        tgtRect.top > svgRect.bottom + 20
      ) return;
      newLines.push({
        id,
        x1: srcRect.right - svgRect.left,
        y1: srcRect.top + srcRect.height / 2 - svgRect.top,
        x2: tgtRect.left - svgRect.left,
        y2: tgtRect.top + tgtRect.height / 2 - svgRect.top,
        isSelected,
        isHovered,
        isArrayConnection,
      });
    };

    // ── Regular field mappings ──────────────────────────────────────────────────
    for (const m of fieldMappings) {
      if (!m.source || !m.target) continue;
      const srcEl = sourceRefs.get(m.source);
      const tgtEl = targetRefs.get(m.target);
      if (!srcEl || !tgtEl) continue;
      addLine(
        m.id,
        srcEl,
        tgtEl,
        selectedId === m.id,
        hoveredPath === m.source || hoveredPath === m.target
      );
    }

    // ── Array mapping inner-field connections (violet) ─────────────────────────
    for (const am of arrayMappings) {
      const fullSrc = getFullSourcePath(am.id, arrayMappings);
      const fullTgt = getFullTargetPrefix(am.id, arrayMappings);
      for (const m of am.mappings) {
        if (!m.source || !m.target) continue;
        const srcPath = `${fullSrc}[*].${m.source}`;
        const tgtPath = `${fullTgt}[*].${m.target}`;
        const srcEl = sourceRefs.get(srcPath);
        const tgtEl = targetRefs.get(tgtPath);
        if (!srcEl || !tgtEl) continue;
        addLine(
          `${am.id}-${m.id}`,
          srcEl,
          tgtEl,
          false,
          hoveredPath === srcPath || hoveredPath === tgtPath,
          true
        );
      }
    }

    setLines(newLines);
  }, [fieldMappings, arrayMappings, selectedId, hoveredPath, sourceRefs, targetRefs]);

  // Recompute on scroll / resize
  useEffect(() => {
    const schedule = () => {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = requestAnimationFrame(computeLines);
    };

    schedule();
    sourceScroll?.addEventListener('scroll', schedule, { passive: true });
    targetScroll?.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule, { passive: true });

    return () => {
      cancelAnimationFrame(rafRef.current);
      sourceScroll?.removeEventListener('scroll', schedule);
      targetScroll?.removeEventListener('scroll', schedule);
      window.removeEventListener('resize', schedule);
    };
  }, [computeLines, sourceScroll, targetScroll]);

  const buildPath = (x1: number, y1: number, x2: number, y2: number): string => {
    const cx = (x1 + x2) / 2;
    return `M ${x1} ${y1} C ${cx} ${y1} ${cx} ${y2} ${x2} ${y2}`;
  };

  return (
    <svg
      ref={svgRef}
      className="pointer-events-none absolute inset-0 w-full h-full overflow-visible z-10"
      style={{ mixBlendMode: 'normal' }}
    >
      <defs>
        <marker id="arrowhead" markerWidth="6" markerHeight="6" refX="6" refY="3" orient="auto">
          <path d="M0,0 L0,6 L6,3 z" fill={CONNECTION_COLORS.default} />
        </marker>
        <marker id="arrowhead-selected" markerWidth="6" markerHeight="6" refX="6" refY="3" orient="auto">
          <path d="M0,0 L0,6 L6,3 z" fill={CONNECTION_COLORS.selected} />
        </marker>
        <marker id="arrowhead-hovered" markerWidth="6" markerHeight="6" refX="6" refY="3" orient="auto">
          <path d="M0,0 L0,6 L6,3 z" fill={CONNECTION_COLORS.hovered} />
        </marker>
        <marker id="arrowhead-array" markerWidth="6" markerHeight="6" refX="6" refY="3" orient="auto">
          <path d="M0,0 L0,6 L6,3 z" fill={CONNECTION_COLORS.array} />
        </marker>
      </defs>
      {lines.map((line) => {
        const isHighlighted = line.isSelected || line.isHovered || hoveredLineId === line.id;
        const color = line.isArrayConnection
          ? CONNECTION_COLORS.array
          : line.isSelected
          ? CONNECTION_COLORS.selected
          : line.isHovered || hoveredLineId === line.id
          ? CONNECTION_COLORS.hovered
          : CONNECTION_COLORS.default;
        const markerId = line.isArrayConnection
          ? 'arrowhead-array'
          : line.isSelected
          ? 'arrowhead-selected'
          : line.isHovered || hoveredLineId === line.id
          ? 'arrowhead-hovered'
          : 'arrowhead';

        return (
          <g key={line.id}>
            {/* Invisible wider hit area */}
            <path
              d={buildPath(line.x1, line.y1, line.x2, line.y2)}
              fill="none"
              stroke="transparent"
              strokeWidth={12}
              style={{ pointerEvents: 'stroke', cursor: 'pointer' }}
              onMouseEnter={() => setHoveredLineId(line.id)}
              onMouseLeave={() => setHoveredLineId(null)}
              onClick={() => dispatch(selectMapping(line.id))}
              onDoubleClick={() => dispatch(removeFieldMapping(line.id))}
            />
            {/* Visible path */}
            <path
              d={buildPath(line.x1, line.y1, line.x2, line.y2)}
              fill="none"
              stroke={color}
              strokeWidth={isHighlighted ? 2 : 1.5}
              strokeDasharray={line.isSelected ? undefined : undefined}
              markerEnd={`url(#${markerId})`}
              style={{ pointerEvents: 'none', transition: 'stroke 0.15s, stroke-width 0.15s' }}
            />
            {/* Dot at source end */}
            <circle cx={line.x1} cy={line.y1} r={3} fill={color} style={{ pointerEvents: 'none' }} />
            {/* Dot at target end */}
            <circle cx={line.x2} cy={line.y2} r={3} fill={color} style={{ pointerEvents: 'none' }} />
          </g>
        );
      })}
      {/* Legend */}
      {lines.length === 0 && (
        <text x="50%" y="50%" textAnchor="middle" fill="#cbd5e1" fontSize="11" fontFamily="monospace">
          Drag source fields onto target fields to create connections
        </text>
      )}
    </svg>
  );
};

export default ConnectionCanvas;
