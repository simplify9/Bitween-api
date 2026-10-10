import { Link } from "react-router";
import type { AggregationTarget } from "../../api";
import { Field, Select } from "../ui/forms";
import { AGGREGATION_TARGET_LABEL } from "./aggregationTargets";

const TARGET_OPTIONS = (Object.keys(AGGREGATION_TARGET_LABEL) as AggregationTarget[]).map((value) => ({
  value,
  label: AGGREGATION_TARGET_LABEL[value],
}));

/**
 * The two things that make an aggregation what it is: whose exchanges it collects, and
 * which of their files the roll-up links to.
 *
 * Shared by the subscription's studio card and the create dialog, so the wording a person
 * reads while making one is the wording they read afterwards. The source is display-only
 * here and picked by the caller instead — it is fixed at creation by the backend, whose
 * `AggregationForId` has a private setter and is skipped by the configuration applier.
 */
export function AggregationFields({
  source,
  target,
  onTargetChange,
  disabled,
}: {
  /**
   * The subscription being rolled up. `null` while one is still being chosen; omit it
   * entirely when the caller shows the source itself and only the collected-file half
   * is left to ask.
   */
  source?: { id: number; name: string } | null;
  target: AggregationTarget;
  onTargetChange: (target: AggregationTarget) => void;
  disabled?: boolean;
}) {
  return (
    <div className="space-y-4">
      {source !== undefined && (
      <Field
        label="Rolls up"
        hint="Fixed once created. Only its successful exchanges are collected — a failed one is never picked up, even if a later retry succeeds."
      >
        {source ? (
          <p className="flex h-9.5 items-center text-sm">
            <Link
              to={`/subscriptions/${source.id}`}
              className="font-medium text-ink-800 hover:text-crimson-700 hover:underline"
            >
              {source.name}
            </Link>
          </p>
        ) : (
          <p className="flex h-9.5 items-center text-sm text-ink-500">Not set</p>
        )}
      </Field>
      )}

      <Field
        label="Collects"
        htmlFor="agg-target"
        hint="Links only — the documents are not combined. Your mapper or delivery does that."
      >
        <Select
          id="agg-target"
          value={target}
          disabled={disabled}
          title="Which file of each collected exchange the roll-up links to: what arrived, what the mapper produced, or what the destination replied."
          onChange={(e) => onTargetChange(e.target.value as AggregationTarget)}
          options={TARGET_OPTIONS}
        />
      </Field>
    </div>
  );
}
