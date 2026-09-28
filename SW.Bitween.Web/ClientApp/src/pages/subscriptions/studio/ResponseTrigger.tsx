import { Checkbox } from "../../../components/ui/forms";
import { EntryPointsTable } from "./Overview";
import type { EntryPoint } from "./model";

/**
 * A response subscription's Trigger node: which subscriptions hand it their response, and
 * whether a bad one counts.
 *
 * Shared by its studio page and its create page, so the node says the same thing in both.
 * The feeders are not chosen here — each one picks this subscription in its own Response
 * step, because that is where the response is.
 */
export function ResponseTrigger({
  feeders,
  runOnBadResponses,
  onChange,
  disabled,
}: {
  feeders: EntryPoint[];
  runOnBadResponses: boolean;
  onChange: (runOnBadResponses: boolean) => void;
  disabled: boolean;
}) {
  return (
    <div className="space-y-4">
      <EntryPointsTable
        rows={feeders}
        empty="No subscription hands its response here yet, so it never runs. Pick this one in another subscription's Response step."
      />
      <Checkbox
        label="Also run on a bad response"
        description="Off, it only runs when the delivery that fed it succeeded. On, it also gets the error bodies — an HTTP 4xx, say — so its mapper has to be able to read them."
        checked={runOnBadResponses}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
      />
    </div>
  );
}
