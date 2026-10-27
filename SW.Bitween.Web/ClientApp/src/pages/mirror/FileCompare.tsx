import { useEffect, useRef } from "react";
import { useQuery } from "@tanstack/react-query";
import { EditorState, type Extension } from "@codemirror/state";
import { EditorView, lineNumbers } from "@codemirror/view";
import { MergeView } from "@codemirror/merge";
import { LoadingBlock, ProblemText } from "../../components/ui/basics";
import { languageFor } from "../../lib/adapterSource";

const editorTheme = EditorView.theme({
  "&": { maxHeight: "28rem", backgroundColor: "#ffffff", fontSize: "12px" },
  ".cm-scroller": { fontFamily: "var(--font-mono)", overflow: "auto" },
  "&.cm-focused": { outline: "none" },
});

/**
 * Prod's file and staging's side by side: what differs highlighted, long runs that are the same folded
 * away. Prod's is staging's faked copy, like everything of prod's here. JSON and XML written on one line
 * are laid out first, so the two line up field by field.
 */
export default function FileCompare({ prodUrl, stagingUrl, format }: { prodUrl: string | null; stagingUrl: string | null; format: string | null }) {
  const files = useQuery({
    queryKey: ["mirror", "fileCompare", prodUrl, stagingUrl],
    queryFn: () => Promise.all([read(prodUrl), read(stagingUrl)]),
    staleTime: Infinity,
  });
  const host = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!files.data || !host.current) return;
    const [a, b] = files.data.map((text) => laidOut(text, format));
    const language = languageFor(`file.${format ?? "txt"}`);
    const extensions: Extension[] = [editorTheme, EditorState.readOnly.of(true), EditorView.editable.of(false), lineNumbers(), EditorView.lineWrapping];
    if (language) extensions.push(language);
    const view = new MergeView({
      a: { doc: a, extensions },
      b: { doc: b, extensions },
      parent: host.current,
      highlightChanges: true,
      gutter: true,
      collapseUnchanged: { margin: 3, minSize: 8 },
    });
    return () => view.destroy();
  }, [files.data, format]);

  if (files.isPending) return <LoadingBlock label="Reading both files…" />;
  if (files.isError) return <ProblemText>{files.error instanceof Error ? files.error.message : "The files couldn't be read."}</ProblemText>;
  return (
    <div className="overflow-hidden rounded-lg border border-ink-200" data-testid="file-compare">
      <div className="grid grid-cols-2 border-b border-ink-200 bg-ink-50 text-[11.5px] font-medium text-ink-600">
        <span className="px-3 py-1" title="Staging's copy of prod's file: every value faked">Prod's, faked</span>
        <span className="border-l border-ink-200 px-3 py-1">Staging's</span>
      </div>
      <div ref={host} />
    </div>
  );
}

/**
 * Valid JSON indented token by token, so every value stays as written: parsing and writing it again
 * turned 1.0 into 1 and rounded long ids, hiding the very differences being compared.
 */
function indented(json: string): string {
  const out: string[] = [];
  let depth = 0;
  const line = () => out.push("\n", "  ".repeat(depth));
  for (let i = 0; i < json.length; i++) {
    const c = json[i];
    if (c === '"') {
      let end = i + 1;
      while (json[end] !== '"') end += json[end] === "\\" ? 2 : 1;
      out.push(json.slice(i, end + 1));
      i = end;
    } else if (c === "{" || c === "[") {
      let next = i + 1;
      while (/\s/.test(json[next] ?? "")) next++;
      if (json[next] === (c === "{" ? "}" : "]")) {
        out.push(c, json[next]);
        i = next;
      } else {
        depth++;
        out.push(c);
        line();
      }
    } else if (c === "}" || c === "]") {
      depth--;
      line();
      out.push(c);
    } else if (c === ",") {
      out.push(c);
      line();
    } else if (c === ":") out.push(": ");
    else if (!/\s/.test(c)) out.push(c);
  }
  return out.join("");
}

async function read(url: string | null): Promise<string> {
  if (!url) return "";
  const response = await fetch(url);
  if (!response.ok) throw new Error(`A file couldn't be read: the server answered ${response.status}. It may have been removed since.`);
  return response.text();
}

/** JSON indented, XML one element to a line when it came on one; anything else as it is. */
export function laidOut(text: string, format: string | null): string {
  if (format === "json") {
    try {
      JSON.parse(text);
    } catch {
      return text;
    }
    return indented(text);
  }
  if (format === "xml" && !text.trim().includes("\n")) {
    let depth = 0;
    return text
      .trim()
      .replace(/>\s*</g, ">\n<")
      .split("\n")
      .map((line) => {
        if (line.startsWith("</")) depth = Math.max(0, depth - 1);
        const indented = "  ".repeat(depth) + line;
        // An opening tag whose element goes on past this line: not a closing, self-closing, declaration or one-line element.
        if (/^<[^/?!][^>]*>$/.test(line) && !line.endsWith("/>")) depth++;
        return indented;
      })
      .join("\n");
  }
  return text;
}
