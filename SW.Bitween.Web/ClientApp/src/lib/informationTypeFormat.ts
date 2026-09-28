import type { InformationTypeFormat } from "../api";

/** Why a CSV or Other type has no promoted properties and no filters — shown wherever that bites. */
export const CARRIED_FORMAT_NOTE =
  "Bitween doesn't read the content of this format: there are no promoted properties, and only subscriptions and routes without a filter pick its messages up.";

/** Every format a type can declare, in the order the pickers offer them. */
export const INFORMATION_TYPE_FORMATS: { value: InformationTypeFormat; label: string }[] = [
  { value: "Json", label: "JSON" },
  { value: "Xml", label: "XML" },
  { value: "Csv", label: "CSV" },
  { value: "Other", label: "Other" },
];

export const formatLabel = (format: InformationTypeFormat): string =>
  INFORMATION_TYPE_FORMATS.find((f) => f.value === format)?.label ?? format;

/**
 * Whether Bitween reads payloads of this format. CSV and Other are carried, never read — the
 * same rule the backend's filter applies.
 */
export const readsContent = (format: InformationTypeFormat): boolean => format === "Json" || format === "Xml";
