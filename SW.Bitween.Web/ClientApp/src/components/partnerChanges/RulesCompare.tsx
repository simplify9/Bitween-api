import { useEffect, useRef } from "react";
import { EditorState, type Extension } from "@codemirror/state";
import { EditorView, lineNumbers } from "@codemirror/view";
import { MergeView } from "@codemirror/merge";
import { json } from "@codemirror/lang-json";

const editorTheme = EditorView.theme({
  "&": { maxHeight: "28rem", backgroundColor: "#ffffff", fontSize: "12px" },
  ".cm-scroller": { fontFamily: "var(--font-mono)", overflow: "auto" },
  "&.cm-focused": { outline: "none" },
});

export const prettyRules = (rules: string) => {
  if (!rules) return "";
  try {
    return JSON.stringify(JSON.parse(rules), null, 2);
  } catch {
    return rules;
  }
};

/**
 * Two sets of mapping rules side by side, what differs highlighted and long runs that are the same
 * folded away: what a partner gets now, and what their change would make it.
 */
export default function RulesCompare({
  before,
  after,
  beforeLabel,
  afterLabel,
}: {
  before: string;
  after: string;
  beforeLabel: string;
  afterLabel: string;
}) {
  const host = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!host.current) return;
    const extensions: Extension[] = [
      editorTheme,
      EditorState.readOnly.of(true),
      EditorView.editable.of(false),
      lineNumbers(),
      EditorView.lineWrapping,
      json(),
    ];
    const view = new MergeView({
      a: { doc: prettyRules(before), extensions },
      b: { doc: prettyRules(after), extensions },
      parent: host.current,
      highlightChanges: true,
      gutter: true,
      collapseUnchanged: { margin: 3, minSize: 8 },
    });
    return () => view.destroy();
  }, [before, after]);

  return (
    <div className="overflow-hidden rounded-lg border border-ink-200" data-testid="rules-compare">
      <div className="grid grid-cols-2 border-b border-ink-200 bg-ink-50 text-[11.5px] font-medium text-ink-600">
        <span className="px-3 py-1">{beforeLabel}</span>
        <span className="border-l border-ink-200 px-3 py-1">{afterLabel}</span>
      </div>
      <div ref={host} />
    </div>
  );
}
