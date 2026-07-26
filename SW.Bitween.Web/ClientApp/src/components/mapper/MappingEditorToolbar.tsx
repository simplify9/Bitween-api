import React, { useMemo } from "react";
import { useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, RefreshCw, Trash2, Undo2, Redo2 } from "lucide-react";
import {
  useMappingEditorState,
  useMappingEditorDispatch,
  autoMatch,
  clearAll,
  redo,
  undo,
  setSelectedPartner,
} from "../../lib/mapping/MappingEditorContext";
import { api } from "../../api";

// ─── Mode toggle button ───────────────────────────────────────────────────────

const ModeTab: React.FC<{
  active: boolean;
  onClick: () => void;
  label: string;
  icon?: React.ReactNode;
}> = ({ active, onClick, label, icon }) => (
  <button
    onClick={onClick}
    className={[
      'flex items-center gap-1.5 px-3 py-1 rounded text-xs font-medium transition border',
      active
        ? 'bg-crimson-600 text-white border-crimson-600 shadow-sm'
        : 'bg-white text-ink-600 border-ink-300 hover:border-crimson-400 hover:text-crimson-600',
    ].join(' ')}
  >
    {icon}
    {label}
  </button>
);

// ─── MappingEditorToolbar ─────────────────────────────────────────────────────

export interface MappingEditorToolbarProps {
  subscriptionId: number;
  isSaving: boolean;
  saveSuccess: boolean;
  handleModeChange: (mode: 'visual' | 'manual') => void;
  handleValidate: () => void;
  handleSave: () => void | Promise<void>;
}

const MappingEditorToolbar: React.FC<MappingEditorToolbarProps> = ({
  subscriptionId,
  isSaving,
  saveSuccess,
  handleModeChange,
  handleValidate,
  handleSave,
}) => {
  const navigate = useNavigate();
  const dispatch = useMappingEditorDispatch();
  const { mode, fieldMappings, arrayMappings, past, future, selectedPartnerId } = useMappingEditorState();

  // Sync local dropdown state when selectedPartnerId changes (including reset to null)
  const { data: partners } = useQuery({ queryKey: ["partners"], queryFn: () => api.listPartners() });
  // listPartners() is a light row (no adapterProperties) — fetch the selected partner's
  // properties separately so the "Partner" mode datalist actually has options to suggest.
  const { data: adapterProperties, isFetching: isPartnerFetching } = useQuery({
    queryKey: ["partner-adapter-properties", selectedPartnerId],
    queryFn: () => api.getPartnerAdapterProperties(selectedPartnerId!),
    enabled: selectedPartnerId != null,
  });

  const handlePartnerChange = (idStr: string) => {
    const pid = idStr ? Number(idStr) : null;
    if (pid == null) {
      dispatch(setSelectedPartner(null, {}));
    } else {
      dispatch(setSelectedPartner(pid, {}));
    }
    // adapterProperties effect below handles dispatch when the fetch resolves
  };

  React.useEffect(() => {
    if (selectedPartnerId == null) return;
    if (isPartnerFetching) return;
    dispatch(setSelectedPartner(selectedPartnerId, adapterProperties ?? {}));
  }, [adapterProperties, selectedPartnerId, isPartnerFetching]);

  const assignedFieldCount = useMemo(
    () => fieldMappings.filter((m) => m.target && (Boolean(m.source) || m.fixedValue !== undefined)).length,
    [fieldMappings]
  );

  const isVisualMode = mode === 'visual';
  const isManualMode = mode === 'manual';

  return (
    <div className="flex items-center gap-3 px-4 py-2.5 border-b border-ink-200 bg-white flex-shrink-0 shadow-sm">
      {/* Back */}
      <button
        onClick={() => navigate(`/subscriptions/${subscriptionId}`)}
        className="flex items-center gap-1.5 text-xs text-ink-500 hover:text-ink-800 border border-ink-200 rounded px-2 py-1 transition mr-1"
      >
        <ArrowLeft size={13} /> Back
      </button>

      <div className="h-5 w-px bg-ink-200" />

      {/* Title */}
      <span className="font-bold text-ink-800 text-sm tracking-tight">
        Mapping Editor
      </span>
      <span className="text-xs text-ink-400">
        {assignedFieldCount} mappings ·{' '}
        {arrayMappings.length} array loops
      </span>

      <div className="h-5 w-px bg-ink-200" />

      {/* Mode toggle */}
      <div className="flex items-center gap-1">
        <ModeTab
          active={isVisualMode}
          onClick={() => handleModeChange('visual')}
          label="Visual"
          icon={<span className="text-[10px]">⛶</span>}
        />
        <ModeTab
          active={isManualMode}
          onClick={() => handleModeChange('manual')}
          label="Manual"
          icon={<span className="text-[10px]">{'{}'}</span>}
        />
      </div>

      <div className="h-5 w-px bg-ink-200" />

      {/* Undo / redo */}
      <button
        onClick={() => dispatch(undo())}
        disabled={past.length === 0}
        className="p-1.5 rounded hover:bg-ink-100 disabled:opacity-30 transition text-ink-600"
        title="Undo (Ctrl+Z)"
      >
        <Undo2 size={16} />
      </button>
      <button
        onClick={() => dispatch(redo())}
        disabled={future.length === 0}
        className="p-1.5 rounded hover:bg-ink-100 disabled:opacity-30 transition text-ink-600"
        title="Redo (Ctrl+Y)"
      >
        <Redo2 size={16} />
      </button>

      <div className="h-5 w-px bg-ink-200" />

      {/* Actions */}
      {isVisualMode && (
        <>
          <button
            onClick={() => dispatch(autoMatch())}
            className="text-xs border border-ink-300 rounded px-2.5 py-1 hover:bg-ink-50 transition"
            title="Auto-match fields by name similarity"
          >
            <RefreshCw className="inline mr-1" size={11} />
            Auto-match
          </button>
          <button
            onClick={() => dispatch(clearAll())}
            className="text-xs border border-crimson-200 text-crimson-500 rounded px-2.5 py-1 hover:bg-crimson-50 transition"
            title="Clear all mappings"
          >
            <Trash2 className="inline mr-1" size={11} />
            Clear
          </button>
        </>
      )}

      <button
        onClick={handleValidate}
        className="text-xs border border-warn-100 text-warn-700 rounded px-2.5 py-1 hover:bg-warn-100 transition"
      >
        Validate
      </button>

      {/* Partner selector — visual mode only — for testing partner-scoped mappings */}
      {isVisualMode && (
        <div className="flex items-center gap-1.5">
          <span className="text-[10px] text-ink-400 font-medium uppercase tracking-wide flex-shrink-0">Test partner</span>
          <select
            className="text-xs border border-ink-200 rounded px-2 py-1 focus:outline-none focus:border-crimson-400 bg-white text-ink-700 max-w-[160px]"
            value={selectedPartnerId ?? ''}
            onChange={(e) => handlePartnerChange(e.target.value)}
          >
            <option value="">— none —</option>
            {(partners ?? []).map((p) => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
          {selectedPartnerId && (
            <span className="text-[10px] text-ok-600 font-medium flex-shrink-0">
              {Object.keys(adapterProperties ?? {}).length} props
            </span>
          )}
        </div>
      )}

      <div className="ml-auto flex items-center gap-2">
        {saveSuccess && (
          <span className="text-xs text-ok-600 font-medium animate-pulse">
            ✓ Saved
          </span>
        )}
        <button
          onClick={() => void handleSave()}
          disabled={isSaving}
          className="text-xs bg-crimson-600 text-white rounded px-4 py-1.5 hover:bg-crimson-700 transition font-medium disabled:opacity-50"
        >
          {isSaving ? 'Saving…' : 'Save'}
        </button>
      </div>
    </div>
  );
};

export default MappingEditorToolbar;
