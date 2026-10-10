import { useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import CodeMirror from "@uiw/react-codemirror";
import { EditorView } from "@codemirror/view";
import { type Extension } from "@codemirror/state";
import { CheckCircle2, CircleSlash, FilePlus, Play, Rocket, Save, Trash2, XCircle } from "lucide-react";
import { api, ApiRequestError, type AdapterDraft, type DraftBuild, type DraftRun } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { BackLink } from "../../components/ui/BackLink";
import { Badge, Button, FormError, LoadingBlock } from "../../components/ui/basics";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { languageFor } from "../../lib/adapterSource";
import { useLeaveGuard } from "../../lib/useLeaveGuard";
import { usePageTitle } from "../../lib/pageTitle";

const LANGUAGE_LABEL = { python: "Python", node: "JavaScript", typescript: "TypeScript" } as const;

const editorTheme = EditorView.theme({
  "&": { height: "100%", minHeight: "28rem", backgroundColor: "#ffffff", fontSize: "12.5px" },
  ".cm-scroller": { fontFamily: "var(--font-mono)", overflow: "auto" },
  "&.cm-focused": { outline: "none" },
});

/** What a kind's first command is called with, for the try panel to start from. */
const SAMPLE_INPUT: Record<string, string> = {
  Handle: JSON.stringify({ Data: '{"orderId":"SO-1"}', Filename: "order.json" }, null, 2),
  Validate: JSON.stringify({ Data: '{"orderId":"SO-1"}', Filename: "order.json" }, null, 2),
  GetFile: "example-1",
  DeleteFile: "example-1",
};

type Panel = "check" | "try" | "publish";

/**
 * Writing a Python or JavaScript adapter in Bitween: its files, built and checked against the
 * Bitween contract on the server, tried with real settings, and published as a version that is not
 * current until someone makes it so. Settings typed here are sent with each call and never stored.
 */
export default function AdapterEditorPage() {
  const { id } = useParams();
  const draftId = Number(id);
  const draft = useQuery({ queryKey: keys.adapterDraft(draftId), queryFn: () => api.getAdapterDraft(draftId) });
  usePageTitle(draft.data?.adapterId);

  if (draft.isPending) return <LoadingBlock label="Opening the draft…" />;
  if (draft.isError) return <p className="text-sm text-danger-700">{draft.error.message}</p>;
  return <Editor key={draft.data.id} draft={draft.data} />;
}

function Editor({ draft }: { draft: AdapterDraft }) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const canPublish = useSessionCan("adapter-source.operate");

  const [files, setFiles] = useState<Record<string, string>>(draft.files);
  const [savedFiles, setSavedFiles] = useState<Record<string, string>>(draft.files);
  const paths = useMemo(() => Object.keys(files).sort(), [files]);
  const [selected, setSelected] = useState(() => paths.find((p) => /^main\.(py|js|ts)$/.test(p)) ?? paths[0]);
  const dirty = JSON.stringify(files) !== JSON.stringify(savedFiles);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState("");
  // The hash of what this editor last loaded or saved: a save is refused if the draft has moved on
  // since, so two people editing one draft don't silently overwrite each other.
  const [baseHash, setBaseHash] = useState(draft.filesHash);
  const [conflict, setConflict] = useState("");
  const [panel, setPanel] = useState<Panel>("check");
  const [settings, setSettings] = useState<Record<string, string>>({});
  const [build, setBuild] = useState<DraftBuild | null>(null);
  const [newFile, setNewFile] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  const save = async (overwrite = false) => {
    if (!dirty) return true;
    setSaving(true);
    setSaveError("");
    setConflict("");
    try {
      const saved = await api.saveAdapterDraft(draft.id, files, overwrite ? undefined : baseHash);
      setSavedFiles(files);
      if (saved?.filesHash) setBaseHash(saved.filesHash);
      void queryClient.invalidateQueries({ queryKey: keys.adapterDrafts });
      return true;
    } catch (e) {
      if (e instanceof ApiRequestError && e.code === "DRAFT_CHANGED") setConflict(e.message);
      else setSaveError(e instanceof Error ? e.message : "The draft couldn't be saved.");
      return false;
    } finally {
      setSaving(false);
    }
  };

  /** Their version, in place of these edits: the page opens the draft afresh. */
  const loadTheirs = async () => {
    const theirs = await queryClient.fetchQuery({
      queryKey: keys.adapterDraft(draft.id),
      queryFn: () => api.getAdapterDraft(draft.id),
      staleTime: 0,
    });
    setFiles(theirs.files);
    setSavedFiles(theirs.files);
    setBaseHash(theirs.filesHash);
    if (!(selected in theirs.files)) setSelected(Object.keys(theirs.files).sort()[0]);
    setConflict("");
  };

  // Ctrl/Cmd+S saves, as in any editor.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === "s") {
        e.preventDefault();
        void save();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  });

  const extensions = useMemo(() => {
    const list: Extension[] = [editorTheme];
    const language = selected ? languageFor(selected) : null;
    if (language) list.push(language);
    return list;
  }, [selected]);

  const addFile = () => {
    const path = (newFile ?? "").trim();
    if (!path || path in files) return;
    setFiles({ ...files, [path]: "" });
    setSelected(path);
    setNewFile(null);
  };

  const removeFile = (path: string) => {
    const { [path]: _, ...rest } = files;
    setFiles(rest);
    if (selected === path) setSelected(Object.keys(rest).sort()[0]);
  };

  const { leave, dialog: leaveDialog } = useLeaveGuard(dirty);

  return (
    <div>
      {leaveDialog}
      <BackLink to="/adapters?tab=installed" label="Installed adapters" />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-semibold text-ink-900">
          <code className="font-mono">{draft.adapterId}</code>
        </h1>
        <Badge tone="neutral">{LANGUAGE_LABEL[draft.language]}</Badge>
        {draft.kind && <Badge tone="neutral">{draft.kind}</Badge>}
        <span className="text-[12.5px] text-ink-500">
          {draft.baseVersion ? `Draft from v${draft.baseVersion}` : "New adapter, not yet published"}
        </span>
        <div className="ml-auto flex gap-2">
          {dirty && <Badge tone="crimson">Unsaved</Badge>}
          <Button size="sm" variant="primary" disabled={!dirty} busy={saving} onClick={() => void save()}>
            <Save className="size-4" aria-hidden />
            Save
          </Button>
          <Button size="sm" variant="danger" onClick={() => setDeleting(true)}>
            <Trash2 className="size-4" aria-hidden />
            Delete draft
          </Button>
        </div>
      </div>
      <FormError>{saveError}</FormError>
      {conflict && (
        <div role="alert" className="mb-3 rounded-lg bg-warn-100 px-3 py-2 text-sm text-warn-700">
          <p>{conflict}</p>
          <div className="mt-2 flex gap-2">
            <Button size="sm" onClick={() => void loadTheirs()}>
              Load their version
            </Button>
            <Button size="sm" variant="danger" busy={saving} onClick={() => void save(true)}>
              Save mine anyway
            </Button>
          </div>
        </div>
      )}

      <div className="grid gap-4 xl:grid-cols-[13rem_minmax(0,1fr)_24rem]">
        <section aria-label="Files" className="rounded-xl border border-ink-200 bg-white py-1">
          <ul>
            {paths.map((path) => (
              <li key={path} className="group flex items-center">
                <button
                  type="button"
                  aria-current={path === selected ? "true" : undefined}
                  onClick={() => setSelected(path)}
                  className={`min-w-0 flex-1 truncate px-3 py-1 text-left font-mono text-[12px] ${
                    path === selected ? "bg-crimson-50 text-ink-900" : "text-ink-700 hover:bg-ink-50"
                  }`}
                  title={path}
                >
                  {path}
                  {files[path] !== savedFiles[path] && <span className="text-crimson-600"> •</span>}
                </button>
                {path !== "adapter.json" && (
                  <button
                    type="button"
                    aria-label={`Remove ${path}`}
                    onClick={() => removeFile(path)}
                    className="px-2 text-ink-300 opacity-0 group-hover:opacity-100 hover:text-danger-600"
                  >
                    <Trash2 className="size-3.5" />
                  </button>
                )}
              </li>
            ))}
          </ul>
          {newFile === null ? (
            <button
              type="button"
              onClick={() => setNewFile("")}
              className="mt-1 flex w-full items-center gap-1.5 px-3 py-1.5 text-[12.5px] text-crimson-700 hover:bg-ink-50"
            >
              <FilePlus className="size-3.5" aria-hidden />
              New file
            </button>
          ) : (
            <form
              className="px-2 py-1.5"
              onSubmit={(e) => {
                e.preventDefault();
                addFile();
              }}
            >
              <TextInput
                aria-label="New file name"
                autoFocus
                placeholder="helpers.py"
                value={newFile}
                onChange={(e) => setNewFile(e.target.value)}
                onBlur={() => !newFile && setNewFile(null)}
              />
            </form>
          )}
        </section>

        <section aria-label="Code" className="min-w-0 overflow-hidden rounded-xl border border-ink-200 bg-white">
          {selected ? (
            <div data-testid="draft-editor" data-path={selected}>
              <CodeMirror
                value={files[selected] ?? ""}
                extensions={extensions}
                onChange={(value) => setFiles((f) => ({ ...f, [selected]: value }))}
                basicSetup={{ foldGutter: false }}
              />
            </div>
          ) : (
            <p className="p-4 text-sm text-ink-500">No file selected.</p>
          )}
        </section>

        <section aria-label="Check, try and publish" className="rounded-xl border border-ink-200 bg-white">
          <div role="tablist" className="flex border-b border-ink-100">
            {(
              [
                ["check", "Check"],
                ["try", "Try"],
                ...(canPublish ? ([["publish", "Publish"]] as const) : []),
              ] as const
            ).map(([value, label]) => (
              <button
                key={value}
                type="button"
                role="tab"
                aria-selected={panel === value}
                onClick={() => setPanel(value)}
                className={`-mb-px flex-1 border-b-2 px-3 py-2 text-[13px] font-medium ${
                  panel === value ? "border-crimson-600 text-ink-900" : "border-transparent text-ink-500 hover:text-ink-800"
                }`}
              >
                {label}
              </button>
            ))}
          </div>
          <div className="space-y-4 p-4">
            <SettingsForm build={build} settings={settings} onChange={setSettings} />
            {panel === "check" && (
              <CheckPanel draftId={draft.id} settings={settings} save={save} build={build} onBuilt={setBuild} />
            )}
            {panel === "try" && <TryPanel draftId={draft.id} kind={draft.kind} settings={settings} save={save} build={build} />}
            {panel === "publish" && canPublish && (
              <PublishPanel draft={draft} settings={settings} save={save} onBuilt={setBuild} />
            )}
          </div>
        </section>
      </div>

      {deleting && (
        <ConfirmDialog
          title="Delete this draft?"
          body="Its code is gone once deleted. Versions already published from it stay published."
          confirmLabel="Delete draft"
          onConfirm={async () => {
            await api.deleteAdapterDraft(draft.id);
            await queryClient.invalidateQueries({ queryKey: keys.adapterDrafts });
            setSavedFiles(files); // nothing left to warn about
            leave(() => navigate("/adapters?tab=installed"));
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}

/**
 * The settings the adapter declares, as its last build reported them. Sent with each check, try
 * and publish, and never stored — a test key typed here stays in this page.
 */
function SettingsForm({
  build,
  settings,
  onChange,
}: {
  build: DraftBuild | null;
  settings: Record<string, string>;
  onChange: (s: Record<string, string>) => void;
}) {
  if (!build?.settings) {
    return <p className="text-[12.5px] text-ink-500">Check the draft to see the settings it declares.</p>;
  }
  if (build.settings.length === 0) return <p className="text-[12.5px] text-ink-500">It declares no settings.</p>;
  return (
    <fieldset className="space-y-2.5">
      <legend className="mb-1 text-[12px] font-semibold tracking-wide text-ink-500 uppercase">Settings to run it with</legend>
      {build.settings.map((s) => (
        <Field key={s.name} label={s.name + (s.required ? " *" : "")} hint={s.description ?? undefined} htmlFor={`setting-${s.name}`}>
          <TextInput
            id={`setting-${s.name}`}
            type={s.secret ? "password" : "text"}
            autoComplete="off"
            placeholder={s.default ?? ""}
            value={settings[s.name] ?? ""}
            onChange={(e) => onChange({ ...settings, [s.name]: e.target.value })}
          />
        </Field>
      ))}
      <p className="text-[12px] text-ink-500">Used for this page's calls only; never saved.</p>
    </fieldset>
  );
}

const filled = (settings: Record<string, string>) => Object.fromEntries(Object.entries(settings).filter(([, v]) => v !== ""));

function CheckPanel({
  draftId,
  settings,
  save,
  build,
  onBuilt,
}: {
  draftId: number;
  settings: Record<string, string>;
  save: () => Promise<boolean>;
  build: DraftBuild | null;
  onBuilt: (b: DraftBuild) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const run = async () => {
    setBusy(true);
    setError("");
    try {
      if (!(await save())) return;
      onBuilt(await api.buildAdapterDraft(draftId, filled(settings)));
    } catch (e) {
      setError(e instanceof Error ? e.message : "The check couldn't run.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3">
      <Button variant="primary" busy={busy} onClick={() => void run()}>
        <CheckCircle2 className="size-4" aria-hidden />
        Save and check
      </Button>
      <FormError>{error}</FormError>
      {build && <BuildReport build={build} />}
    </div>
  );
}

function BuildReport({ build }: { build: DraftBuild }) {
  return (
    <div aria-label="Check results" className="space-y-2 text-[12.5px]">
      {build.problems.length > 0 && (
        <ul className="space-y-1 rounded-lg bg-danger-50 p-2.5 text-danger-800">
          {build.problems.map((p) => (
            <li key={p} className="whitespace-pre-wrap">
              {p}
            </li>
          ))}
        </ul>
      )}
      {build.warnings.map((w) => (
        <p key={w} className="rounded-lg bg-warn-100 p-2 text-warn-700">
          {w}
        </p>
      ))}
      {build.succeeded && (
        <p className={build.conforms ? "font-medium text-ok-800" : "font-medium text-danger-700"}>
          {build.checks.length === 0 ? "Builds." : build.conforms ? "Builds and conforms." : "Builds, but doesn't conform."}
        </p>
      )}
      <ul className="space-y-1">
        {build.checks.map((c) => (
          <li key={c.name} className="flex gap-1.5">
            {c.outcome === "Passed" ? (
              <CheckCircle2 className="mt-0.5 size-3.5 shrink-0 text-ok-600" aria-label="Passed" />
            ) : c.outcome === "Failed" ? (
              <XCircle className="mt-0.5 size-3.5 shrink-0 text-danger-600" aria-label="Failed" />
            ) : (
              <CircleSlash className="mt-0.5 size-3.5 shrink-0 text-ink-500" aria-label="Skipped" />
            )}
            <span>
              <span className="text-ink-800">{c.name}</span>
              {c.detail && <span className="block text-ink-500">{c.detail}</span>}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

function TryPanel({
  draftId,
  kind,
  settings,
  save,
  build,
}: {
  draftId: number;
  kind: string | null;
  settings: Record<string, string>;
  save: () => Promise<boolean>;
  build: DraftBuild | null;
}) {
  const commands = build?.commands?.length
    ? build.commands
    : kind === "validator"
      ? ["Validate"]
      : kind === "receiver"
        ? ["Initialize", "ListFiles", "GetFile", "DeleteFile", "Finalize"]
        : ["Handle"];
  const [command, setCommand] = useState(commands[0]);
  const [input, setInput] = useState(SAMPLE_INPUT[commands[0]] ?? "");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<DraftRun | null>(null);
  const [error, setError] = useState("");

  const run = async () => {
    setBusy(true);
    setError("");
    setResult(null);
    try {
      if (!(await save())) return;
      setResult(await api.tryAdapterDraft(draftId, filled(settings), command, input));
    } catch (e) {
      setError(e instanceof Error ? e.message : "The call couldn't run.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3">
      <Field label="Command" htmlFor="try-command">
        <Select
          id="try-command"
          value={command}
          onChange={(e) => {
            setCommand(e.target.value);
            setInput(SAMPLE_INPUT[e.target.value] ?? "");
          }}
          options={commands.map((c) => ({ value: c, label: c }))}
        />
      </Field>
      <Field label="Input" hint="JSON, or plain text for a command that takes text." htmlFor="try-input">
        <textarea
          id="try-input"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          rows={6}
          className="w-full rounded-lg border border-ink-200 px-3 py-2 font-mono text-[12px] focus:border-focus-400 focus:outline-none"
        />
      </Field>
      <Button variant="primary" busy={busy} onClick={() => void run()}>
        <Play className="size-4" aria-hidden />
        Save and run
      </Button>
      <FormError>{error}</FormError>
      {result && (
        <div aria-label="Try result" className="space-y-1.5 text-[12.5px]">
          {result.problems.map((p) => (
            <p key={p} className="rounded-lg bg-danger-50 p-2 whitespace-pre-wrap text-danger-800">
              {p}
            </p>
          ))}
          {result.error && <p className="rounded-lg bg-danger-50 p-2 whitespace-pre-wrap text-danger-800">{result.error}</p>}
          {result.succeeded && (
            <pre className="max-h-64 overflow-auto rounded-lg bg-ink-50 p-2.5 font-mono text-[12px] whitespace-pre-wrap text-ink-800">
              {prettyJson(result.output)}
            </pre>
          )}
        </div>
      )}
    </div>
  );
}

function prettyJson(text: string | null) {
  if (!text) return "(nothing)";
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
}

function PublishPanel({
  draft,
  settings,
  save,
  onBuilt,
}: {
  draft: AdapterDraft;
  settings: Record<string, string>;
  save: () => Promise<boolean>;
  onBuilt: (b: DraftBuild) => void;
}) {
  const queryClient = useQueryClient();
  const [mode, setMode] = useState(draft.baseVersion ? "patch" : "custom");
  const [custom, setCustom] = useState(draft.baseVersion ? "" : "1.0.0");
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [published, setPublished] = useState<string | null>(null);
  const [promoted, setPromoted] = useState(false);

  const publish = async () => {
    setBusy(true);
    setError("");
    try {
      if (!(await save())) return;
      const result = await api.publishAdapterDraft(draft.id, {
        version: mode === "custom" ? custom.trim() : mode,
        releaseNotes: notes,
        settings: filled(settings),
      });
      onBuilt(result.build);
      if (!result.published) {
        setError("It wasn't published: it must build and conform first. The Check tab shows why.");
        return;
      }
      setPublished(result.version!);
      await queryClient.invalidateQueries({ queryKey: ["adapters"] });
      await queryClient.invalidateQueries({ queryKey: keys.adapterDraft(draft.id) });
    } catch (e) {
      setError(e instanceof Error ? e.message : "It couldn't be published.");
    } finally {
      setBusy(false);
    }
  };

  const promote = async () => {
    setBusy(true);
    setError("");
    try {
      await api.promoteAdapter(draft.adapterId, published!);
      setPromoted(true);
      await queryClient.invalidateQueries({ queryKey: ["adapters"] });
    } catch (e) {
      setError(e instanceof Error ? e.message : "It couldn't be made current.");
    } finally {
      setBusy(false);
    }
  };

  if (published) {
    return (
      <div aria-label="Published" className="space-y-3 text-[13px]">
        <p className="font-medium text-ok-800">Published v{published}.</p>
        <p className="text-ink-600">
          {promoted
            ? `v${published} is now current: subscriptions that don't pin a version run it.`
            : "It isn't current yet: subscriptions keep running what they ran until it is made current, or pinned on one of them."}
        </p>
        {!promoted && (
          <Button variant="primary" busy={busy} onClick={() => void promote()}>
            Make v{published} current
          </Button>
        )}
        <FormError>{error}</FormError>
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <Field label="Version" htmlFor="publish-version">
        <Select
          id="publish-version"
          value={mode}
          onChange={(e) => setMode(e.target.value)}
          options={[
            ...(draft.baseVersion
              ? [
                  { value: "patch", label: "Next patch" },
                  { value: "minor", label: "Next minor" },
                  { value: "major", label: "Next major" },
                ]
              : []),
            { value: "custom", label: "Exactly…" },
          ]}
        />
      </Field>
      {mode === "custom" && (
        <Field label="Version number" htmlFor="publish-number">
          <TextInput id="publish-number" value={custom} onChange={(e) => setCustom(e.target.value)} placeholder="1.0.0" />
        </Field>
      )}
      <Field label="Release notes" htmlFor="publish-notes">
        <textarea
          id="publish-notes"
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          rows={3}
          className="w-full rounded-lg border border-ink-200 px-3 py-2 text-[13px] focus:border-focus-400 focus:outline-none"
        />
      </Field>
      <p className="text-[12px] text-ink-500">
        It is built and checked first, with the settings above. Publishing doesn't change what runs; making it current
        does.
      </p>
      <Button variant="primary" busy={busy} onClick={() => void publish()}>
        <Rocket className="size-4" aria-hidden />
        Publish
      </Button>
      <FormError>{error}</FormError>
    </div>
  );
}
