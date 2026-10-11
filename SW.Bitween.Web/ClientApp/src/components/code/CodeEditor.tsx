import { useEffect, useRef } from "react";
import { languageOf, monaco } from "./monacoSetup";
import type { EditorProblem } from "./problems";

export interface CodeEditorProps {
  /** Keeps two drafts' files apart in Monaco's shared model registry. */
  scope: string;
  files: Record<string, string>;
  /** The file shown; null shows nothing. */
  active: string | null;
  theme: "dark" | "light";
  /** What the last Check said about particular lines, drawn as squiggles in the code. */
  checkProblems: EditorProblem[];
  /** A request to put the cursor somewhere — a click in Problems. `seq` makes a repeat count. */
  revealAt: { path: string; line: number; column: number; seq: number } | null;
  onChange: (path: string, text: string) => void;
  onCursor: (at: { line: number; column: number }) => void;
  /** Everything the language services currently say, across every file. */
  onProblems: (problems: EditorProblem[]) => void;
  onSave: () => void;
  onQuickOpen: () => void;
}

const CHECK_OWNER = "bitween-check";

const SEVERITY: Record<EditorProblem["severity"], monaco.MarkerSeverity> = {
  error: monaco.MarkerSeverity.Error,
  warning: monaco.MarkerSeverity.Warning,
  info: monaco.MarkerSeverity.Info,
};

/**
 * One Monaco editor over every file of a draft. Each file is its own model, kept for as long as
 * the file exists, so switching tabs keeps each file's undo history, cursor and scroll position —
 * as in VS Code — and the TypeScript service sees every file at once, so imports between them
 * resolve. Loaded on demand (see AdapterEditorPage): Monaco is the heaviest thing in the app.
 */
export default function CodeEditor(props: CodeEditorProps) {
  const host = useRef<HTMLDivElement>(null);
  const editor = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const models = useRef(new Map<string, monaco.editor.ITextModel>());
  const viewStates = useRef(new Map<string, monaco.editor.ICodeEditorViewState | null>());
  const shown = useRef<string | null>(null);
  // The callbacks Monaco holds on to read the latest props, not the first render's.
  const latest = useRef(props);
  latest.current = props;

  const uriOf = (path: string) => monaco.Uri.parse(`file:///${props.scope}/${path}`);
  const pathOf = (uri: monaco.Uri) => {
    const prefix = `/${props.scope}/`;
    return uri.path.startsWith(prefix) ? uri.path.slice(prefix.length) : null;
  };

  // The editor, once.
  useEffect(() => {
    const ed = monaco.editor.create(host.current!, {
      model: null,
      theme: latest.current.theme === "dark" ? "bitween-dark" : "bitween-light",
      automaticLayout: true,
      fontFamily: '"JetBrains Mono Variable", ui-monospace, "SF Mono", monospace',
      fontSize: 13,
      lineHeight: 20,
      fontLigatures: true,
      minimap: { enabled: true, renderCharacters: false, scale: 1 },
      bracketPairColorization: { enabled: true },
      guides: { bracketPairs: "active", indentation: true },
      stickyScroll: { enabled: true },
      smoothScrolling: true,
      cursorBlinking: "smooth",
      cursorSmoothCaretAnimation: "on",
      renderWhitespace: "selection",
      scrollBeyondLastLine: false,
      padding: { top: 8 },
      fixedOverflowWidgets: true,
      formatOnPaste: true,
      linkedEditing: true,
      "semanticHighlighting.enabled": true,
    });
    editor.current = ed;

    ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => latest.current.onSave());
    ed.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyP, () => latest.current.onQuickOpen());
    const cursor = ed.onDidChangeCursorPosition((e) =>
      latest.current.onCursor({ line: e.position.lineNumber, column: e.position.column }),
    );

    const report = () => {
      const problems: EditorProblem[] = [];
      for (const marker of monaco.editor.getModelMarkers({})) {
        const path = pathOf(marker.resource);
        if (path === null || marker.owner === CHECK_OWNER) continue;
        problems.push({
          path,
          line: marker.startLineNumber,
          column: marker.startColumn,
          message: marker.message,
          severity:
            marker.severity === monaco.MarkerSeverity.Error
              ? "error"
              : marker.severity === monaco.MarkerSeverity.Warning
                ? "warning"
                : "info",
          source: marker.source ?? marker.owner,
        });
      }
      latest.current.onProblems(problems);
    };
    const markers = monaco.editor.onDidChangeMarkers(report);

    // Monaco measures its font when it starts; if the web font arrived later, measure again, or the
    // cursor sits a little off the characters it is between.
    void document.fonts?.ready.then(() => monaco.editor.remeasureFonts());

    const owned = models.current;
    return () => {
      cursor.dispose();
      markers.dispose();
      ed.dispose();
      owned.forEach((m) => m.dispose());
      owned.clear();
      editor.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A model per file: new files get one, removed files lose theirs, and text changed from outside
  // (someone else's save, loaded over this one) replaces what the model holds.
  useEffect(() => {
    for (const [path, text] of Object.entries(props.files)) {
      const model = models.current.get(path);
      if (!model) {
        const created = monaco.editor.createModel(text, languageOf(path), uriOf(path));
        created.onDidChangeContent(() => latest.current.onChange(path, created.getValue()));
        models.current.set(path, created);
      } else if (model.getValue() !== text) {
        model.pushEditOperations([], [{ range: model.getFullModelRange(), text }], () => null);
      }
    }
    for (const [path, model] of models.current) {
      if (path in props.files) continue;
      model.dispose();
      models.current.delete(path);
      viewStates.current.delete(path);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.files]);

  // The file shown, with where you were in it.
  useEffect(() => {
    const ed = editor.current;
    if (!ed) return;
    if (shown.current && models.current.has(shown.current)) viewStates.current.set(shown.current, ed.saveViewState());
    const model = props.active ? (models.current.get(props.active) ?? null) : null;
    if (ed.getModel() !== model) {
      ed.setModel(model);
      const state = props.active ? viewStates.current.get(props.active) : null;
      if (state) ed.restoreViewState(state);
    }
    shown.current = props.active;
    if (model) ed.focus();
  }, [props.active, props.files]);

  useEffect(() => {
    monaco.editor.setTheme(props.theme === "dark" ? "bitween-dark" : "bitween-light");
  }, [props.theme]);

  // The Check's findings, on the lines they name.
  useEffect(() => {
    for (const [path, model] of models.current) {
      monaco.editor.setModelMarkers(
        model,
        CHECK_OWNER,
        props.checkProblems
          .filter((p) => p.path === path)
          .map((p) => ({
            startLineNumber: p.line,
            startColumn: p.column,
            endLineNumber: p.line,
            endColumn: model.getLineMaxColumn(Math.min(p.line, model.getLineCount())),
            message: p.message,
            severity: SEVERITY[p.severity],
            source: "Check",
          })),
      );
    }
  }, [props.checkProblems, props.files]);

  useEffect(() => {
    const ed = editor.current;
    const at = props.revealAt;
    if (!ed || !at || at.path !== props.active) return;
    ed.revealLineInCenter(at.line);
    ed.setPosition({ lineNumber: at.line, column: at.column });
    ed.focus();
  }, [props.revealAt, props.active]);

  return <div ref={host} className="h-full w-full" />;
}
