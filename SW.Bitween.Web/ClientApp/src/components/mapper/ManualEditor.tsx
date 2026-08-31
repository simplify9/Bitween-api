import React, { lazy, Suspense, useEffect, useRef } from "react";
import loader from "@monaco-editor/loader";
import {
  useMappingEditorDispatch,
  useMappingEditorState,
  setArrayMappings,
  setFieldMappings,
  setManualTemplate,
  syncManualTemplate,
} from "../../lib/mapping/MappingEditorContext";
import { generateScriban, parseScriban } from "../../lib/mapping/scribanGenerator";

// Pinned to an exact version so the CSP entries in Startup.cs (which allow only
// this path, not all of cdn.jsdelivr.net) stay valid — bump both together.
loader.config({ paths: { vs: "https://cdn.jsdelivr.net/npm/monaco-editor@0.55.1/min/vs" } });

// Monaco is heavy — lazy-load it so it stays out of the main bundle.
const MonacoEditor = lazy(() => import("@monaco-editor/react"));

const SCRIBAN_HINT = `{{- # Scriban template — edit freely -}}
{{- # Use {{ variable.path }} for values, for/end for loops, if/end for filters -}}
`;

const ManualEditor: React.FC = () => {
  const dispatch = useMappingEditorDispatch();
  const { fieldMappings, arrayMappings, manualTemplate, isManualDirty } = useMappingEditorState();
  const editorRef = useRef<any>(null);
  const [parseWarnings, setParseWarnings] = React.useState<string[]>([]);
  const [parseSuccess, setParseSuccess] = React.useState(false);

  // ManualEditor mounts each time the user switches to Manual mode.
  // We rely on handleModeChange in index.tsx to call syncManualTemplate before
  // changing the mode, so by the time this component mounts the template is
  // already up-to-date. This effect is a safety fallback for initial page load
  // where mode starts as 'visual' and has never been set via handleModeChange.
  useEffect(() => {
    if (!isManualDirty) {
      dispatch(syncManualTemplate(generateScriban(fieldMappings, arrayMappings, undefined, undefined)));
    }
  }, []); // Only on mount — ongoing sync is handled by handleModeChange

  const handleEditorChange = (value: string | undefined) => {
    dispatch(setManualTemplate(value ?? ''));
  };

  const handleRegenerateFromVisual = () => {
    const generated = generateScriban(fieldMappings, arrayMappings, undefined, undefined);
    dispatch(syncManualTemplate(generated));
    editorRef.current?.setValue(generated);
    setParseWarnings([]);
    setParseSuccess(false);
  };

  const handleParseBackToVisual = () => {
    const result = parseScriban(manualTemplate);
    setParseWarnings(result.warnings);

    if (result.fieldMappings.length > 0 || result.arrayMappings.length > 0) {
      // Replace both fieldMappings and arrayMappings entirely from the parsed template
      dispatch(
        setFieldMappings(
          result.fieldMappings.map((m, i) => ({ id: `parsed-${i}-${Date.now()}`, ...m }))
        )
      );
      dispatch(
        setArrayMappings(
          result.arrayMappings.map((am, i) => ({
            id: `parsed-am-${i}-${Date.now()}`,
            ...am,
            mappings: am.mappings.map((m, j) => ({ id: `parsed-am-${i}-${j}-${Date.now()}`, ...m })),
          }))
        )
      );
      setParseSuccess(true);
    } else {
      setParseWarnings(['No parseable mappings found. Check your template syntax.']);
    }
  };

  const templateToShow =
    manualTemplate ||
    (fieldMappings.length > 0 || arrayMappings.length > 0
      ? generateScriban(fieldMappings, arrayMappings, undefined, undefined)
      : SCRIBAN_HINT);

  return (
    <div className="flex flex-col h-full">
      {/* Toolbar */}
      <div className="flex items-center gap-2 px-4 py-2 border-b border-ink-200 bg-ink-50 flex-shrink-0">
        <span className="text-xs font-semibold text-ink-600 uppercase tracking-wide">
          Scriban Template
        </span>
        <span className="text-xs text-ink-400">— edit the template directly</span>
        <div className="ml-auto flex items-center gap-2">
          <button
            onClick={handleRegenerateFromVisual}
            className="text-xs border border-ink-300 rounded px-3 py-1 hover:bg-ink-100 transition"
            title="Re-generate template from current visual mappings (will overwrite edits)"
          >
            ↺ Regenerate from Visual
          </button>
          <button
            onClick={handleParseBackToVisual}
            className="text-xs bg-crimson-600 text-white rounded px-3 py-1 hover:bg-crimson-700 transition"
            title="Attempt to parse this template back into visual mappings"
          >
            → Apply to Visual Mode
          </button>
        </div>
      </div>

      {/* Validation messages */}
      {parseWarnings.length > 0 && (
        <div className="flex-shrink-0 px-4 py-2 bg-warn-100 border-b border-warn-100">
          <p className="text-xs font-semibold text-warn-700 mb-1">Parse warnings:</p>
          <ul className="space-y-0.5">
            {parseWarnings.map((w, i) => (
              <li key={i} className="text-xs text-warn-700 font-mono">
                • {w}
              </li>
            ))}
          </ul>
        </div>
      )}
      {parseSuccess && (
        <div className="flex-shrink-0 px-4 py-2 bg-ok-100 border-b border-ok-100">
          <p className="text-xs text-ok-600">
            ✓ Template parsed successfully — switched to Visual mode.
          </p>
        </div>
      )}

      {/* Monaco Editor */}
      <div className="flex-1 min-h-0">
        <Suspense fallback={<div className="flex h-full items-center justify-center text-sm text-ink-400">Loading editor…</div>}>
          <MonacoEditor
            language="handlebars"
            theme="vs"
            value={templateToShow}
            onChange={handleEditorChange}
            onMount={(ed) => {
              editorRef.current = ed;
            }}
            options={{
              minimap: { enabled: true },
              fontSize: 13,
              lineNumbers: 'on',
              wordWrap: 'on',
              scrollBeyondLastLine: false,
              fontFamily: '"Fira Code", "JetBrains Mono", monospace',
              automaticLayout: true,
              formatOnPaste: true,
              tabSize: 2,
              suggest: { snippetsPreventQuickSuggestions: false },
            }}
          />
        </Suspense>
      </div>

      {/* Scriban cheat sheet */}
      <div className="flex-shrink-0 px-4 py-2 border-t border-ink-200 bg-ink-50">
        <div className="flex items-start gap-4 text-xs text-ink-500 font-mono flex-wrap">
          <span>
            <span className="text-crimson-600">{'{{ var.path }}'}</span> value
          </span>
          <span>
            <span className="text-crimson-600">{'{{- for item in arr -}}'}</span> …{' '}
            <span className="text-crimson-600">{'{{- end -}}'}</span> loop
          </span>
          <span>
            <span className="text-warn-700">{'{{- if expr -}}'}</span> …{' '}
            <span className="text-warn-700">{'{{- end -}}'}</span> condition
          </span>
          <span>
            <span className="text-ink-400">{'{{- # comment -}}'}</span>
          </span>
        </div>
      </div>
    </div>
  );
};

export default ManualEditor;
