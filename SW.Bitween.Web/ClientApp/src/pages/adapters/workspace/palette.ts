/**
 * The workspace's colours in each theme - VS Code's own, so the layout reads as the editor people
 * already know, with Bitween's accent in place of VS Code's blue.
 */
export const PALETTE = {
  dark: {
    frame: "bg-[#181818] text-[#cccccc] border-[#2b2b2b]",
    pane: "bg-[#181818]",
    bar: "bg-[#181818] border-[#2b2b2b]",
    tab: "text-[#9d9d9d] hover:bg-[#2a2a2a]",
    tabActive: "bg-[#1e1e1e] text-white",
    row: "text-[#cccccc] hover:bg-[#2a2d2e]",
    rowActive: "bg-[#37373d] text-white",
    muted: "text-[#8b8b8b]",
    heading: "text-[#bbbbbb]",
    input: "bg-[#313131] text-white border-[#3c3c3c] placeholder:text-[#8b8b8b]",
    divider: "border-[#2b2b2b]",
    handle: "hover:bg-crimson-500/60",
    icon: "text-[#c5c5c5] hover:bg-[#ffffff1a]",
    problem: "text-danger-300",
  },
  light: {
    frame: "bg-[#f8f8f8] text-ink-800 border-ink-200",
    pane: "bg-[#f8f8f8]",
    bar: "bg-[#f8f8f8] border-ink-200",
    tab: "text-ink-500 hover:bg-ink-100",
    tabActive: "bg-white text-ink-900",
    row: "text-ink-800 hover:bg-ink-100",
    rowActive: "bg-crimson-50 text-ink-900",
    muted: "text-ink-500",
    heading: "text-ink-600",
    input: "bg-white text-ink-900 border-ink-300 placeholder:text-ink-400",
    divider: "border-ink-200",
    handle: "hover:bg-crimson-400/60",
    icon: "text-ink-600 hover:bg-ink-200",
    problem: "text-danger-700",
  },
} as const;

export type WorkspaceTheme = keyof typeof PALETTE;

/** A language's colour, as file icons show it. */
export function fileTint(path: string): string {
  const ext = path.slice(path.lastIndexOf(".") + 1).toLowerCase();
  const tints: Record<string, string> = {
    py: "text-[#4b8bbe]",
    ts: "text-[#3178c6]",
    tsx: "text-[#3178c6]",
    js: "text-[#e8c33a]",
    mjs: "text-[#e8c33a]",
    cjs: "text-[#e8c33a]",
    json: "text-[#cbcb41]",
    md: "text-[#519aba]",
    txt: "text-[#9d9d9d]",
    toml: "text-[#9c4221]",
    yml: "text-[#cb171e]",
    yaml: "text-[#cb171e]",
  };
  return tints[ext] ?? "text-[#9d9d9d]";
}
