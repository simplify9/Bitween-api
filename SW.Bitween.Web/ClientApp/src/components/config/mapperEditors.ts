import { NATIVE_MAPPER_ID } from "../../lib/nativeMapper/types";

/**
 * Mappers whose mapping is built in the visual editor rather than typed into
 * adapter properties. Both are listed while the old mapper is still in use by
 * running subscriptions; its entry goes when they have been moved over.
 *
 * The old id is spelled out because its module sits outside the app's tsconfig.
 */
const VISUAL_EDITOR_MAPPERS: readonly string[] = ["NativeJSONMapper", NATIVE_MAPPER_ID];

export function usesVisualMappingEditor(adapterId: string | null | undefined): boolean {
  return adapterId != null && VISUAL_EDITOR_MAPPERS.includes(adapterId);
}
