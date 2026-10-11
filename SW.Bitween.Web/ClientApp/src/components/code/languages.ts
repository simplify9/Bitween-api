/** Monaco's language for a file, by name. Anything unknown opens as plain text. */
export function languageOf(path: string): string {
  const name = path.toLowerCase();
  const base = name.slice(name.lastIndexOf("/") + 1);
  if (base === "dockerfile") return "dockerfile";
  if (base === "requirements.txt") return "plaintext";
  const ext = base.includes(".") ? base.slice(base.lastIndexOf(".") + 1) : "";
  const byExt: Record<string, string> = {
    py: "python",
    js: "javascript",
    mjs: "javascript",
    cjs: "javascript",
    jsx: "javascript",
    ts: "typescript",
    mts: "typescript",
    cts: "typescript",
    tsx: "typescript",
    json: "json",
    md: "markdown",
    yml: "yaml",
    yaml: "yaml",
    xml: "xml",
    csproj: "xml",
    cs: "csharp",
    sh: "shell",
    toml: "ini",
    ini: "ini",
    cfg: "ini",
    env: "ini",
    sql: "sql",
    html: "html",
    css: "css",
  };
  return byExt[ext] ?? "plaintext";
}

const LABELS: Record<string, string> = {
  python: "Python",
  javascript: "JavaScript",
  typescript: "TypeScript",
  json: "JSON",
  markdown: "Markdown",
  yaml: "YAML",
  xml: "XML",
  csharp: "C#",
  shell: "Shell",
  ini: "TOML/INI",
  sql: "SQL",
  html: "HTML",
  css: "CSS",
  dockerfile: "Dockerfile",
  plaintext: "Plain text",
};

/** What the status bar calls a file's language. */
export const languageLabel = (path: string) => LABELS[languageOf(path)] ?? languageOf(path);
