/** A folder of the draft, with what is directly in it. Folders exist only as the paths of files. */
export interface FolderNode {
  name: string;
  /** "" for the root, else "src" or "src/lib". */
  path: string;
  folders: FolderNode[];
  files: string[];
}

export function buildTree(paths: string[]): FolderNode {
  const root: FolderNode = { name: "", path: "", folders: [], files: [] };
  for (const path of paths) {
    const parts = path.split("/");
    let at = root;
    for (const part of parts.slice(0, -1)) {
      const folderPath = at.path ? `${at.path}/${part}` : part;
      let next = at.folders.find((f) => f.name === part);
      if (!next) {
        next = { name: part, path: folderPath, folders: [], files: [] };
        at.folders.push(next);
      }
      at = next;
    }
    at.files.push(path);
  }
  const sort = (node: FolderNode) => {
    node.folders.sort((a, b) => a.name.localeCompare(b.name));
    node.files.sort((a, b) => baseName(a).localeCompare(baseName(b)));
    node.folders.forEach(sort);
  };
  sort(root);
  return root;
}

export const baseName = (path: string) => path.slice(path.lastIndexOf("/") + 1);
export const folderOf = (path: string) => (path.includes("/") ? path.slice(0, path.lastIndexOf("/")) : "");

/** A file name the draft can take: relative, no "..", no empty parts, not already there. */
export function pathProblem(path: string, existing: string[]): string | null {
  if (!path) return "Give it a name.";
  if (path.startsWith("/") || path.split("/").some((p) => p === "" || p === "." || p === ".."))
    return "Use a relative path, like helpers.py or lib/helpers.py.";
  if (existing.includes(path)) return "There is already a file with that name.";
  return null;
}
