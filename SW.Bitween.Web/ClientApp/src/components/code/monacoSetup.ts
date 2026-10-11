import * as monaco from "monaco-editor";
import EditorWorker from "monaco-editor/editor/editor.worker?worker";
import TsWorker from "monaco-editor/language/typescript/ts.worker?worker";
import JsonWorker from "monaco-editor/language/json/json.worker?worker";
import { languageOf } from "./languages";

/*
 * Monaco — the editor VS Code is built on — bundled with the app rather than fetched from a CDN:
 * the page's Content-Security-Policy allows scripts and workers from this origin only, and an
 * instance on a closed network has no CDN to reach. This module is only loaded by the adapter
 * editor, so nothing here weighs on any other page. Each language's grammar is its own chunk,
 * fetched the first time a file in it is opened, and the TypeScript and JSON workers likewise.
 */

self.MonacoEnvironment = {
  getWorker(_id: string, label: string) {
    if (label === "typescript" || label === "javascript") return new TsWorker();
    if (label === "json") return new JsonWorker();
    return new EditorWorker();
  },
};

// The adapter's imports — the SDK, its dependencies — aren't in the browser, so the language
// service can't resolve them. Saying so on every import line would bury the real problems; the
// Check runs the actual build and reports what really fails.
const IGNORED_DIAGNOSTICS = [
  2307, // Cannot find module
  2792, // Cannot find module, did you mean to set moduleResolution
  7016, // Could not find a declaration file for module
  2875, // JSX tag requires the module path to exist
];
for (const defaults of [monaco.typescript.typescriptDefaults, monaco.typescript.javascriptDefaults]) {
  defaults.setCompilerOptions({
    target: monaco.typescript.ScriptTarget.ESNext,
    module: monaco.typescript.ModuleKind.ESNext,
    moduleResolution: monaco.typescript.ModuleResolutionKind.NodeJs,
    allowJs: true,
    allowNonTsExtensions: true,
    esModuleInterop: true,
    strict: true,
  });
  defaults.setDiagnosticsOptions({ diagnosticCodesToIgnore: IGNORED_DIAGNOSTICS });
  // Every open file is known to the language service, so an import of another file in the draft
  // resolves, completes and type-checks as it would in VS Code.
  defaults.setEagerModelSync(true);
}

// tsconfig and jsconfig are JSON with comments, as TypeScript reads them; everywhere else (package.json
// above all, which npm refuses with a comment in it) a comment is still an error. Nothing is fetched
// to validate against: schemas from the internet aren't reachable from every instance.
monaco.json.jsonDefaults.setDiagnosticsOptions({
  validate: true,
  enableSchemaRequest: false,
  schemas: [
    {
      uri: "bitween://schemas/tsconfig.json",
      fileMatch: ["**/tsconfig*.json", "**/jsconfig*.json", "tsconfig*.json", "jsconfig*.json"],
      schema: { type: "object", allowComments: true, allowTrailingCommas: true },
    },
  ],
});

/** VS Code's own dark and light colours, with Bitween's accent where VS Code puts its blue. */
monaco.editor.defineTheme("bitween-dark", {
  base: "vs-dark",
  inherit: true,
  rules: [],
  colors: {
    "editor.background": "#1e1e1e",
    "editorCursor.foreground": "#f6503d",
    "editor.lineHighlightBackground": "#2a2d2e",
    "editorLineNumber.activeForeground": "#e3e3e3",
    focusBorder: "#f6503d80",
  },
});
monaco.editor.defineTheme("bitween-light", {
  base: "vs",
  inherit: true,
  rules: [],
  colors: {
    "editor.background": "#ffffff",
    "editorCursor.foreground": "#e3311d",
    "editor.lineHighlightBackground": "#f6f3f1",
    focusBorder: "#e3311d80",
  },
});

export { languageOf, monaco };
