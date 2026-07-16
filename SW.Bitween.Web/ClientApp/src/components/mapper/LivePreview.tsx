import type React from "react";
import { useEffect, useRef, useState } from "react";
import { Copy } from "lucide-react";
import { useMappingEditorState } from "../../lib/mapping/MappingEditorContext";
import { generateScriban } from "../../lib/mapping/scribanGenerator";
import { useValuesSetMap } from "./data";

const DEBOUNCE_MS = 600;

const LivePreview: React.FC = () => {
  const { inputJson, outputJson: targetSchemaJson, fieldMappings, arrayMappings, manualTemplate, mode, validationErrors, partnerAdapterProperties, selectedPartnerId } = useMappingEditorState();

  const valuesSetMap = useValuesSetMap();
  // The prototype can't execute Scriban — show the generated TEMPLATE instead of
  // executed output. Derived synchronously (debounced) from generateScriban; no API.
  const [template, setTemplate] = useState<string | null>(null);

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);

    debounceRef.current = setTimeout(() => {
      const hasPartnerProps = Object.keys(partnerAdapterProperties).length > 0;
      // If no source JSON but partner props exist, use {} as base so __partner__ can still be injected
      const effectiveInputJson = !inputJson.trim() && hasPartnerProps ? '{}' : inputJson;

      if (!effectiveInputJson.trim() && !selectedPartnerId) {
        setTemplate(null);
        return;
      }

      // Use manual template if in manual mode, otherwise generate from state
      const generated =
        mode === 'manual' && manualTemplate
          ? manualTemplate
          : generateScriban(fieldMappings, arrayMappings, valuesSetMap, targetSchemaJson || undefined, effectiveInputJson || undefined);

      setTemplate(generated);
    }, DEBOUNCE_MS);

    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [inputJson, fieldMappings, arrayMappings, manualTemplate, mode, valuesSetMap, partnerAdapterProperties, selectedPartnerId]);

  const formatted = template;

  return (
    <div className="flex flex-col h-full bg-ink-50">
      <div className="flex items-center gap-2 px-4 py-2 border-b border-ink-200 bg-white flex-shrink-0">
        <span className="text-xs font-semibold text-ink-600 uppercase tracking-wide">
          Generated template
        </span>
        <span className="text-xs text-ink-400">— the Scriban this mapping will run on the server</span>
        {formatted && (
          <button
            className="ml-auto flex items-center gap-1 text-xs text-ink-500 hover:text-ink-700 border border-ink-200 rounded px-2 py-0.5 transition"
            onClick={() => navigator.clipboard.writeText(formatted)}
            title="Copy to clipboard"
          >
            <Copy size={12} /> Copy
          </button>
        )}
      </div>

      {/* Prototype limitation note */}
      <div className="flex-shrink-0 px-4 py-2 bg-warn-100 border-b border-warn-100">
        <p className="text-xs text-warn-700 font-mono">Executed output isn't available in the prototype — this shows the generated Scriban template. Running it against real data needs the backend preview endpoint.</p>
      </div>

      {/* Validation errors */}
      {validationErrors.length > 0 && (
        <div className="flex-shrink-0 px-4 py-2 bg-crimson-50 border-b border-crimson-100">
          {validationErrors.map((e, i) => (
            <p
              key={i}
              className={`text-xs font-mono ${e.type === 'error' ? 'text-crimson-600' : 'text-warn-700'}`}
            >
              {e.type === 'error' ? '✗' : '⚠'} {e.message}
            </p>
          ))}
        </div>
      )}

      {/* Generated template */}
      <div className="flex-1 overflow-auto">
        {formatted === null ? (
          <div className="flex items-center justify-center h-full text-center px-4">
            <p className="text-xs text-ink-400">Provide source JSON and mappings to see the output</p>
          </div>
        ) : (
          <pre className="px-4 py-3 text-xs font-mono text-ink-700 leading-5 whitespace-pre-wrap break-all">
            {formatted}
          </pre>
        )}
      </div>
    </div>
  );
};

export default LivePreview;
