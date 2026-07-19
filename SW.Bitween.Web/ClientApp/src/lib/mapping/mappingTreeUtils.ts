import { ArrayMapping, FieldMapping } from './types';
import { TreeNode } from './mappingPreview';

// ─── Helper: check if a field mapping has any value assigned ────────────────────

export function isFieldMappingPopulated(m: FieldMapping | undefined): boolean {
  if (!m) return false;
  return !!(
    m.source ||
    m.fixedValue !== undefined ||
    m.partnerPropKey !== undefined ||
    (m.globalSetId && m.globalKey) ||
    (m.lookupDictionary?.entries?.length ?? 0) > 0 ||
    m.valuesSetId
  );
}

// ─── Helper: resolve full target prefix for a nested ArrayMapping ─────────────

export function getFullTargetPrefix(amId: string, allMappings: ArrayMapping[]): string {
  const am = allMappings.find((m) => m.id === amId);
  if (!am) return '';
  if (!am.parentArrayId) return am.target;
  return `${getFullTargetPrefix(am.parentArrayId, allMappings)}[*].${am.target}`;
}

// ─── Helper: build tree from flat target paths ────────────────────────────────

export function buildMappingTree(targetPaths: string[]): TreeNode[] {
  const root: TreeNode[] = [];

  for (const path of targetPaths) {
    const parts = path.split('.');
    insertNode(root, parts, 0, '');
  }

  return root;
}

export function insertNode(
  nodes: TreeNode[],
  parts: string[],
  depth: number,
  parentPath: string
): void {
  if (parts.length === 0) return;
  const rawPart = parts[0];
  const isArray = rawPart.includes('[*]') || rawPart.includes('[]');
  // When rawPart is purely '[*]' (root array), key would be empty — use 'root' to match
  // the synthetic node created by buildTree() so isRootArrayNode detection works correctly.
  const key = rawPart.replace(/\[\*\]|\[\]/g, '') || 'root';
  const currentPath = parentPath ? `${parentPath}.${rawPart}` : rawPart;

  let existing = nodes.find((n) => n.key === key);
  if (parts.length === 1) {
    if (!existing) {
      nodes.push({ key, path: currentPath, type: 'leaf', children: [] });
    }
    return;
  }

  if (!existing) {
    existing = { key, path: currentPath, type: isArray ? 'array' : 'object', children: [] };
    nodes.push(existing);
  } else if (existing.type === 'leaf') {
    // Upgrade leaf to container when it gains children
    existing.type = isArray ? 'array' : 'object';
  }
  insertNode(existing.children, parts.slice(1), depth + 1, currentPath);
}
