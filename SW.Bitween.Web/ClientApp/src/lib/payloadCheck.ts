import type { InformationTypeFormat } from "../api";

/**
 * Whether a payload reads as the format its information type says, and if not, why — in the
 * parser's words. A malformed payload used to be accepted by hand and then fail inside the
 * pipeline as a Newtonsoft error. Only JSON and XML are checked; other formats are taken as they come.
 */
export function payloadProblem(text: string, format: InformationTypeFormat | null | undefined): string | null {
  if (!text.trim() || !format) return null;
  if (format === "Json") {
    try {
      JSON.parse(text);
      return null;
    } catch (e) {
      return `This isn't valid JSON: ${e instanceof Error ? e.message : String(e)}`;
    }
  }
  if (format === "Xml") {
    const doc = new DOMParser().parseFromString(text, "application/xml");
    const error = doc.getElementsByTagName("parsererror")[0];
    return error ? `This isn't valid XML: ${(error.textContent ?? "").split("\n").find((l) => l.trim()) ?? "it doesn't parse"}` : null;
  }
  return null;
}
