import { useQuery } from "@tanstack/react-query";
import { api } from "../../api";
import { keys } from "../../api/queryKeys";
import { useRules } from "../../lib/nativeMapper/RulesEditorContext";
import type { ValueSource, ValueTypeName } from "../../lib/nativeMapper/types";
import { RowInput, RowSelect, RowSuggestInput } from "./rowControls";

/**
 * The source paths a rule may read, by where they are read from.
 *
 * `document` is null at the top level, where there is no enclosing entry and the
 * two would be the same list.
 */
export interface SourcePaths {
  /** Relative to the entry this rule sits in, or the whole document at the top. */
  entry: string[];
  /** Absolute, from the top of the document. Null when that is already `entry`. */
  document: string[] | null;
}

/**
 * Where a path is read from, as its own control.
 *
 * Inside a list, "sku" alone cannot say whether it means the line's sku or the
 * document's, so the scope has to be asked. It used to be folded into the option
 * values of a single dropdown; with a path that can be typed there are no option
 * values to fold it into, and asking it plainly is clearer anyway.
 */
const ENTRY = "entry";
const DOCUMENT = "doc";

/** The one input a row shows for whichever kind of source it uses. */
export function ValueCell({
  source,
  paths,
  valueType,
  emptyPathLabel,
  onChange,
}: {
  source: ValueSource;
  paths: SourcePaths;
  /** The type the field is written as, which decides how a fixed value is typed in. */
  valueType?: ValueTypeName;
  /**
   * What an empty path means, where it means something rather than nothing.
   *
   * On a list of plain values it is "the entry itself" — the ordinary case, and what
   * the scaffolder writes. Given a label, the empty box reads as that answer instead
   * of as a box nobody has filled in yet.
   */
  emptyPathLabel?: string;
  onChange: (source: ValueSource) => void;
}) {
  const { testPartnerId } = useRules();

  const { data: globalSets } = useQuery({
    queryKey: keys.valueSets.list,
    queryFn: () => api.listValueSets(),
    enabled: source.kind === "global",
  });

  // The keys of whichever partner the preview is running as. Without one there is
  // nothing to suggest, and the box is then simply a box — a partner the mapping
  // will run against later may have keys this one does not.
  const { data: partnerProps } = useQuery({
    queryKey: keys.partners.adapterProperties(testPartnerId),
    queryFn: () => api.getPartnerAdapterProperties(testPartnerId!),
    enabled: source.kind === "partner" && testPartnerId !== null,
  });

  switch (source.kind) {
    case "path":
    case "rootPath": {
      const path = source.path ?? "";
      const fromDocument = source.kind === "rootPath";
      const known = fromDocument ? (paths.document ?? paths.entry) : paths.entry;
      // Flagged rather than refused: the sample is an aid, and a path it does not
      // happen to contain — a field absent from this one document, or any field at
      // all when the sample's list is empty — may still be the right mapping.
      const orphan = path !== "" && !known.includes(path);
      // Not a hint about what to type but the answer itself, so it is set in the
      // colour of a value rather than the grey of a placeholder.
      //
      // Only ever true of a path read from the entry. Switch the box to "document"
      // and an empty path is the whole incoming document, not the entry — a mapping
      // that writes the same document into every slot of the list, and the last
      // thing to label as the ordinary answer.
      const meansWholeEntry =
        emptyPathLabel !== undefined && path === "" && source.kind === "path";

      return (
        <div className="flex min-w-0 flex-1 items-center gap-1">
          {/* Only inside a list is this a real question. At the top level the entry
              and the document are the same thing, so asking would be two names for
              one answer. */}
          {paths.document && (
            <RowSelect
              className="w-20 flex-shrink-0"
              aria-label="Read from"
              title="Whether the path is read from this entry or from the top of the document"
              value={fromDocument ? DOCUMENT : ENTRY}
              onChange={(e) =>
                onChange({
                  kind: e.target.value === DOCUMENT ? "rootPath" : "path",
                  path,
                })
              }
              options={[
                { value: ENTRY, label: "entry" },
                { value: DOCUMENT, label: "document" },
              ]}
            />
          )}

          <RowSuggestInput
            className={`min-w-0 flex-1 font-mono ${
              meansWholeEntry ? "placeholder:text-ink-700" : ""
            }`}
            aria-label="Source field"
            placeholder={meansWholeEntry ? emptyPathLabel : "order.customer"}
            title={
              meansWholeEntry
                ? `Empty means ${emptyPathLabel}. Type a path to read a field of it instead.`
                : paths.document
                  ? "A path on this entry, or on the whole document — type it, or pick from the sample"
                  : "A path into the incoming document — type it, or pick one from the sample"
            }
            suggestions={known}
            value={path}
            onChange={(e) => onChange({ ...source, path: e.target.value })}
          />

          {orphan && (
            <span
              className="flex-shrink-0 text-[11px] text-warn-700"
              title="The sample document has no such path, so the preview cannot confirm it"
            >
              ⚠
            </span>
          )}
        </div>
      );
    }

    case "fixed":
      // Typed to the field, so a boolean is a choice of two and a number cannot be
      // given letters — rather than free text that fails once the exchange runs.
      if (valueType === "boolean")
        return (
          <RowSelect
            className="min-w-0 flex-1"
            aria-label="Fixed value"
            value={String(source.value ?? "")}
            onChange={(e) => onChange({ kind: "fixed", value: e.target.value })}
            options={[
              { value: "", label: "— pick —" },
              { value: "true", label: "true" },
              { value: "false", label: "false" },
            ]}
          />
        );

      return (
        <RowInput
          className="min-w-0 flex-1"
          aria-label="Fixed value"
          placeholder={valueType === "number" ? "0" : "WEB"}
          inputMode={valueType === "number" ? "decimal" : undefined}
          value={String(source.value ?? "")}
          onChange={(e) => onChange({ kind: "fixed", value: e.target.value })}
        />
      );

    // Nothing to fill in — the question is the whole rule. Said in words rather than left as
    // an empty box, which would read as a control nobody had got round to filling in.
    case "count":
      return (
        <span
          className="min-w-0 flex-1 truncate px-1 text-[11px] text-ink-500"
          title="How many rows the source list produced — the same number wherever it is read in this list. Entries written by hand are not counted."
        >
          how many rows
        </span>
      );

    case "partner": {
      const known = Object.keys(partnerProps ?? {});
      // Flagged the same way a source path is: the partner being previewed not
      // having this key does not make the key wrong, since the mapping runs against
      // whichever partner the exchange belongs to.
      const unknown = known.length > 0 && source.key !== "" && !known.includes(source.key ?? "");

      return (
        <div className="flex min-w-0 flex-1 items-center gap-1">
          <RowSuggestInput
            className="min-w-0 flex-1 font-mono"
            aria-label="Partner property key"
            placeholder="merchantSlug"
            title={
              known.length > 0
                ? "A key from the partner's adapter properties — type it, or pick one the previewed partner has"
                : "A key from the partner's adapter properties. Choose a partner above to see which it has."
            }
            suggestions={known}
            value={source.key ?? ""}
            onChange={(e) => onChange({ kind: "partner", key: e.target.value })}
          />
          {unknown && (
            <span
              className="flex-shrink-0 text-[11px] text-warn-700"
              title="The partner being previewed has no such property"
            >
              ⚠
            </span>
          )}
        </div>
      );
    }

    case "global": {
      // The set rows carry their own values, so the key is a list to choose from
      // rather than something to type and get wrong.
      const chosenSet = globalSets?.find((s) => s.id === source.setId);
      const keyOptions = Object.keys(chosenSet?.values ?? {});

      return (
        <div className="flex min-w-0 flex-1 gap-1">
          <RowSelect
            className="min-w-0 flex-1"
            aria-label="Global values set"
            value={source.setId ?? ""}
            onChange={(e) =>
              // A key from the old set almost certainly is not in the new one.
              onChange({ kind: "global", setId: e.target.value, key: "" })
            }
            options={[
              { value: "", label: "Set…" },
              ...(globalSets ?? []).map((s) => ({ value: s.id, label: s.name || s.id })),
            ]}
          />
          {keyOptions.length > 0 ? (
            <RowSelect
              className="min-w-0 flex-1 font-mono"
              aria-label="Global value key"
              value={source.key ?? ""}
              onChange={(e) => onChange({ ...source, kind: "global", key: e.target.value })}
              options={[
                { value: "", label: "Key…" },
                ...keyOptions.map((k) => ({ value: k, label: k })),
              ]}
            />
          ) : (
            <RowInput
              className="min-w-0 flex-1 font-mono"
              aria-label="Global value key"
              placeholder={source.setId ? "no values" : "key"}
              disabled={!source.setId}
              value={source.key ?? ""}
              onChange={(e) => onChange({ ...source, kind: "global", key: e.target.value })}
            />
          )}
        </div>
      );
    }
  }
}
