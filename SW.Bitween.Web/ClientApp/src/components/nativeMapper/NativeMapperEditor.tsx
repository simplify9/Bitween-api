import { useCallback, useMemo, useState, type ReactNode } from "react";
import { useNavigate, useParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, Check, Eraser, Eye, EyeOff, Link2, Redo2, Undo2 } from "lucide-react";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import {
  RulesEditorProvider,
  useRules,
  useRulesDispatch,
} from "../../lib/nativeMapper/RulesEditorContext";
import {
  coverageByPath,
  parseSample,
  readablePaths,
} from "../../lib/nativeMapper/documentTree";
import { everyFieldRule } from "../../lib/nativeMapper/rulesReducer";
import type { MatchTally } from "../../lib/nativeMapper/scaffold";
import { DOCUMENT_FORMATS, type DocumentFormatId } from "../../lib/nativeMapper/types";
import { Button, FormError } from "../ui/basics";
import { ConnectionLines, type Connection } from "../ui/ConnectionLines";
import { ConfirmDialog } from "../ui/overlays";
import { Select } from "../ui/forms";
import { BuildFromSample } from "./BuildFromSample";
import { OutputPanel } from "./OutputPanel";
import { PreviewPanel } from "./PreviewPanel";
import { SourcePanel } from "./SourcePanel";
import {
  useMappingLoader,
  useMappingPreview,
  useMappingSave,
  useMappingShortcuts,
  type MappingTarget,
} from "./useMapping";

/**
 * @param target What the editor reads and writes. Omitted, it takes the subscription in
 * the route — which is how the `/subscriptions/:id/mapper` page has always opened it.
 * A create page passes a draft instead, so a mapping can be built before the
 * subscription it belongs to exists.
 * @param onClose Where "Back" goes. Omitted, it returns to the subscription's page; a
 * draft has no page to return to, so its host closes the overlay instead.
 */
export default function NativeMapperEditor({
  target,
  onClose,
}: {
  target?: MappingTarget;
  onClose?: () => void;
} = {}) {
  return (
    <RulesEditorProvider>
      <Editor target={target} onClose={onClose} />
    </RulesEditorProvider>
  );
}

function Editor({ target, onClose }: { target?: MappingTarget; onClose?: () => void }) {
  const { id } = useParams<{ id: string }>();
  const subscriptionId = Number(id);
  const navigate = useNavigate();
  const resolved: MappingTarget = target ?? { kind: "subscription", subscriptionId };

  const { rules, sourceSample, selectedId, hoveredPath, loadError, dirty, match, testPartnerId } =
    useRules();
  const dispatch = useRulesDispatch();

  const { partnerId } = useMappingLoader(resolved);

  const { isPreviewing } = useMappingPreview(testPartnerId ?? partnerId);

  // Hiding the preview gives the rules the whole width, which is what a big mapping
  // wants once it is built and being read rather than checked.
  const [showPreview, setShowPreview] = useState(true);
  const { save, isSaving, justSaved, saveError, replacing } = useMappingSave(resolved);

  // Everything that saves goes through here, so the keyboard cannot slip past the
  // question the Save button asks.
  const [confirming, setConfirming] = useState(false);
  const requestSave = useCallback(
    () => (replacing ? setConfirming(true) : void save()),
    [replacing, save],
  );
  useMappingShortcuts(requestSave);

  const sample = useMemo(
    () => parseSample(sourceSample, rules.sourceFormat),
    [sourceSample, rules.sourceFormat],
  );
  const sourcePaths = useMemo(() => readablePaths(sample.root), [sample.root]);

  /** Which source paths a rule already reads, for the dot beside each field. */
  const assignedPaths = useMemo(() => {
    const paths = new Set<string>();
    for (const { rule } of everyFieldRule(rules))
      if (rule.from.kind === "path" && rule.from.path) paths.add(rule.from.path);
    return paths;
  }, [rules]);

  /** Built once for the whole tree: every source row re-renders on hover. */
  const coverage = useMemo(
    () => coverageByPath(sample.root, assignedPaths),
    [sample.root, assignedPaths],
  );

  /**
   * One curve per rule that reads a field of the document.
   *
   * A rule inside a list reads a path relative to its entry, and the source panel
   * only lists what is readable at the top level — so those have no source row to
   * join and simply draw nothing, rather than joining the wrong one.
   */
  const connections = useMemo(() => {
    const out: Connection[] = [];
    for (const { rule } of everyFieldRule(rules)) {
      if (rule.from.kind !== "path" || !rule.from.path) continue;
      out.push({
        id: rule.id,
        source: rule.from.path,
        target: rule.id,
        emphasis: rule.id === selectedId || rule.from.path === hoveredPath,
      });
    }
    return out;
  }, [rules, selectedId, hoveredPath]);

  /** Clicking a source field fills whichever rule is selected. */
  const pickSourcePath = (path: string) => {
    if (!selectedId) return;
    dispatch({ type: "UPDATE_FIELD", id: selectedId, changes: { from: { kind: "path", path } } });
  };

  if (loadError) {
    return (
      <div className="fixed inset-0 z-40 flex flex-col items-center justify-center gap-4 bg-white px-6">
        <FormError>{loadError}</FormError>
        <p className="max-w-prose text-center text-sm text-ink-600">
          Nothing has been changed. Saving from here would replace the stored rules, so the editor
          will not open them.
        </p>
        <Button onClick={() => (onClose ? onClose() : navigate(`/subscriptions/${subscriptionId}`))}>
          {onClose ? "Back" : "Back to the subscription"}
        </Button>
      </div>
    );
  }

  /** Leaving with rules that were never saved throws them away, so it is asked first. */
  const leave = () => {
    if (dirty && !window.confirm("This mapping has changes that have not been saved. Leave anyway?"))
      return;
    if (onClose) return onClose();
    navigate(`/subscriptions/${subscriptionId}`);
  };

  return (
    <div className="fixed inset-0 z-40 flex flex-col overflow-hidden bg-white">
      {/* ── Toolbar ───────────────────────────────────────────────────────────── */}
      <div className="flex flex-shrink-0 flex-wrap items-center gap-2 border-b border-ink-200 px-3 py-2">
        <button
          type="button"
          onClick={leave}
          className="flex items-center gap-1 rounded px-1.5 py-1 text-sm text-ink-600 hover:bg-ink-50"
        >
          <ArrowLeft size={14} /> Back
        </button>

        <span className="text-sm font-semibold text-ink-900">Mapping</span>

        <div className="ml-2 flex items-center gap-1.5">
          <FormatSelect
            label="From"
            value={rules.sourceFormat}
            onChange={(format) => dispatch({ type: "SET_SOURCE_FORMAT", format })}
          />
          <span aria-hidden className="text-ink-300">
            →
          </span>
          <FormatSelect
            label="To"
            value={rules.targetFormat}
            onChange={(format) => dispatch({ type: "SET_TARGET_FORMAT", format })}
          />
        </div>

        <div className="ml-2">
          <BuildFromSample />
        </div>

        <TestPartnerSelect
          value={testPartnerId}
          onChange={(partner) => dispatch({ type: "SET_TEST_PARTNER", partnerId: partner })}
        />

        <ToolbarButton
          label="Match the source fields"
          title="Point every rule that reads nothing at the source field of the same name"
          icon={<Link2 size={12} aria-hidden />}
          onClick={() => dispatch({ type: "MATCH_SOURCES" })}
        />
        {match && <MatchNote tally={match} />}

        <ToolbarButton
          label="Clear all the rules"
          title="Remove every rule. Undo puts them back."
          icon={<Eraser size={12} aria-hidden />}
          onClick={() => dispatch({ type: "CLEAR_RULES" })}
        />

        <div className="ml-auto flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => setShowPreview((shown) => !shown)}
            aria-pressed={showPreview}
            aria-label={showPreview ? "Hide the preview" : "Show the preview"}
            title={showPreview ? "Hide the preview" : "Show the preview"}
            className="rounded p-1.5 text-ink-500 hover:bg-ink-50"
          >
            {showPreview ? <Eye size={14} /> : <EyeOff size={14} />}
          </button>

          <button
            type="button"
            onClick={() => dispatch({ type: "UNDO" })}
            title="Undo"
            aria-label="Undo"
            className="rounded p-1.5 text-ink-500 hover:bg-ink-50"
          >
            <Undo2 size={14} />
          </button>
          <button
            type="button"
            onClick={() => dispatch({ type: "REDO" })}
            title="Redo"
            aria-label="Redo"
            className="rounded p-1.5 text-ink-500 hover:bg-ink-50"
          >
            <Redo2 size={14} />
          </button>

          {justSaved ? (
            <span className="flex items-center gap-1 text-sm font-medium text-ok-600">
              <Check size={14} /> Saved
            </span>
          ) : (
            <Button variant="primary" busy={isSaving} disabled={!dirty} onClick={requestSave}>
              Save
            </Button>
          )}
        </div>
      </div>

      {saveError && (
        <div className="flex-shrink-0 border-b border-danger-100 bg-danger-50 px-3 py-2">
          <p className="text-xs text-danger-700">{saveError}</p>
        </div>
      )}

      {/* ── Panels ────────────────────────────────────────────────────────────── */}
      <div className="flex flex-1 overflow-hidden">
        <SourcePanel
          root={sample.root}
          parseError={sample.error}
          assignedPaths={assignedPaths}
          coverage={coverage}
          onPick={pickSourcePath}
        />

        {/* Narrow on purpose: the curves are drawn with overflow visible, so they
            reach the rows on either side without a wide empty column. */}
        <div className="relative w-20 flex-shrink-0 border-r border-ink-200 bg-ink-50/40">
          <ConnectionLines
            connections={connections}
            sourceAttribute="data-source-path"
            targetAttribute="data-rule-id"
            watchSelector="[data-mapper-scroll]"
          />
        </div>

        <div className="flex flex-1 divide-x divide-ink-200 overflow-hidden">
          <OutputPanel sourceRoot={sample.root} />
          {showPreview && (
            <div className="w-[38%] min-w-[280px] overflow-hidden">
              <PreviewPanel isPreviewing={isPreviewing} />
            </div>
          )}
        </div>
      </div>

      {/* A rule has to be selected for a source click to land somewhere, so say so
          rather than letting the click appear to do nothing. */}
      <div className="flex-shrink-0 border-t border-ink-200 bg-ink-50 px-3 py-1.5">
        <p className="text-[11px] text-ink-500">
          {sourcePaths.length === 0
            ? "Paste a sample document to see the fields you can map."
            : selectedId
              ? "Drag a source field onto a rule, or click one to fill the selected rule."
              : "Drag a source field onto a rule — or select a rule first, then click a field."}
        </p>
      </div>

      {confirming && replacing && (
        <ConfirmDialog
          title="Replace the mapping this subscription already has?"
          body={
            <>
              This subscription maps with{" "}
              <strong className="font-medium text-ink-800">{replacing}</strong>, and saving
              here switches it to the new mapper. The mapping built in the other editor is
              discarded, and nothing else holds a copy of it.
            </>
          }
          confirmLabel="Replace the mapping"
          onConfirm={async () => {
            await save();
          }}
          onClose={() => setConfirming(false)}
        />
      )}
    </div>
  );
}

/** A toolbar action that looks like the "Build from a sample" trigger beside it. */
function ToolbarButton({
  label,
  title,
  icon,
  onClick,
}: {
  label: string;
  title: string;
  icon: ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      title={title}
      className="flex items-center gap-1 rounded-lg border border-ink-200 bg-white px-2 py-1 text-xs font-medium text-ink-600 hover:border-ink-300 hover:bg-ink-50"
    >
      {icon} {label}
    </button>
  );
}

/** What the last press of "Match the source fields" did. */
function MatchNote({ tally }: { tally: MatchTally }) {
  if (tally.matched === 0 && tally.unmatched === 0)
    return <span className="text-[11px] text-ink-500">Every rule already reads something.</span>;

  return (
    <span className="text-[11px] text-ink-600">
      <span className={tally.matched > 0 ? "text-ok-700" : ""}>
        {tally.matched} matched
      </span>
      {tally.unmatched > 0 && (
        <span
          className="text-ink-500"
          title="Nothing in the sample clearly fitted, or two fields fitted equally well"
        >
          {" · "}
          {tally.unmatched} left empty
        </span>
      )}
    </span>
  );
}

/**
 * Which partner's values to preview with.
 *
 * The property count is on each option because an empty partner and a wrong key
 * both show as a blank field, and this tells the two apart without leaving the page.
 */
function TestPartnerSelect({
  value,
  onChange,
}: {
  value: number | null;
  onChange: (id: number | null) => void;
}) {
  const { data: partners } = useQuery({
    queryKey: keys.partners.list,
    queryFn: () => api.listPartners(),
  });

  return (
    <label className="ml-2 flex items-center gap-1 text-[11px] text-ink-500">
      Preview as
      <Select
        className="h-8 w-44 text-xs"
        aria-label="Preview as partner"
        value={value === null ? "" : String(value)}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        options={[
          { value: "", label: "no partner" },
          ...(partners ?? []).map((p) => ({
            value: String(p.id),
            label: `${p.name} · ${p.propertyKeys.length} properties`,
          })),
        ]}
      />
    </label>
  );
}

function FormatSelect({
  label,
  value,
  onChange,
}: {
  label: string;
  value: DocumentFormatId;
  onChange: (format: DocumentFormatId) => void;
}) {
  return (
    <label className="flex items-center gap-1 text-[11px] text-ink-500">
      {label}
      <Select
        className="h-8 w-24 text-xs"
        aria-label={`${label} format`}
        value={value}
        onChange={(e) => onChange(e.target.value as DocumentFormatId)}
        options={DOCUMENT_FORMATS.map((f) => ({ value: f.id, label: f.label }))}
      />
    </label>
  );
}
