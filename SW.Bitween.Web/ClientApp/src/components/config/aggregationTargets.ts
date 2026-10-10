import type { AggregationTarget } from "../../api";

/**
 * How each choice reads to a person. The enum names the file from the system's side
 * ("Output"); these name it from the source exchange's side, which is the only way to
 * tell them apart without already knowing the pipeline.
 *
 * One definition, three lengths: the dropdown and the studio node and the list column
 * all describe the same setting, and three hand-written wordings drift.
 */
export const AGGREGATION_TARGET_LABEL: Record<AggregationTarget, string> = {
  Input: "What came in",
  Output: "What the mapper produced",
  Response: "What the destination replied",
};

export const AGGREGATION_TARGET_DETAIL: Record<AggregationTarget, string> = {
  Input: "links to what came in",
  Output: "links to what the mapper produced",
  Response: "links to what the destination replied",
};
