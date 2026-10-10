import { ApiRequestError } from "../api/types";

/**
 * A refused save's messages, shared out between the fields they are about and the rest.
 *
 * The server keys each message by the property its validator checked ("Name") or by a code that
 * is about one field ("NAME_TAKEN"); `fields` says which keys belong to which field, compared
 * without regard to case. A message no field claims stays in `rest`, for the form's own error
 * line, so nothing the server said goes unshown and nothing is shown twice. When every message
 * went to a field, `rest` still says so: a save bar can be a long way from the field.
 */
export function splitErrors<F extends string>(
  error: unknown,
  fields: Record<F, string[]>,
): { byField: Partial<Record<F, string>>; rest: string } {
  if (!error) return { byField: {}, rest: "" };
  if (!(error instanceof ApiRequestError) || !error.errors)
    return { byField: {}, rest: error instanceof Error ? error.message : String(error) };

  const owner = new Map<string, F>();
  for (const field of Object.keys(fields) as F[]) for (const key of fields[field]) owner.set(key.toLowerCase(), field);

  const byField: Partial<Record<F, string>> = {};
  const rest: string[] = [];
  for (const [key, messages] of Object.entries(error.errors)) {
    const field = owner.get(key.toLowerCase());
    if (field) byField[field] = [byField[field], ...messages].filter(Boolean).join(" ");
    else rest.push(...messages);
  }
  const unclaimed = [...new Set(rest)].join(" ");
  return { byField, rest: unclaimed || (Object.keys(byField).length ? "Check the fields marked in red." : "") };
}
