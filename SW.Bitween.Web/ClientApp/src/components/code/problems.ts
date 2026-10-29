/** One thing wrong (or worth a look) in a draft, at a place in it when one is known. */
export interface EditorProblem {
  path: string;
  line: number;
  column: number;
  message: string;
  severity: "error" | "warning" | "info";
  /** Who said so: "Check", "ts", "json". */
  source: string;
}

/** A message from a Check, with the place it names when it names one of the draft's files. */
export interface CheckFinding {
  message: string;
  severity: "error" | "warning";
  at: { path: string; line: number; column: number } | null;
}

/**
 * Finds the file and line in a build message, in the forms the compilers write them:
 *
 *   main.py:12:5: E999 SyntaxError …        (python, ruff, tsc --pretty false, gcc-likes)
 *   src/index.ts(12,5): error TS2322: …     (tsc, MSBuild)
 *   File "/build/adapter/main.py", line 12  (a Python traceback)
 *
 * A path only counts if it is one of the draft's files - matched by its end, since the build ran
 * in a directory of its own and reports absolute or prefixed paths.
 */
export function locate(message: string, paths: string[]): CheckFinding["at"] {
  const patterns: RegExp[] = [
    /File "([^"]+)", line (\d+)/,
    /([\w./\\-]+\.\w+)\((\d+),(\d+)\)/,
    /([\w./\\-]+\.\w+):(\d+)(?::(\d+))?/,
  ];
  for (const pattern of patterns) {
    const m = message.match(pattern);
    if (!m) continue;
    const reported = m[1].replace(/\\/g, "/");
    const path = paths
      .filter((p) => reported === p || reported.endsWith(`/${p}`))
      .sort((a, b) => b.length - a.length)[0];
    if (!path) continue;
    return { path, line: Number(m[2]) || 1, column: Number(m[3]) || 1 };
  }
  return null;
}

export function checkFindings(
  build: { problems: string[]; warnings: string[] } | null,
  paths: string[],
): CheckFinding[] {
  if (!build) return [];
  return [
    ...build.problems.map((message) => ({ message, severity: "error" as const, at: locate(message, paths) })),
    ...build.warnings.map((message) => ({ message, severity: "warning" as const, at: locate(message, paths) })),
  ];
}
