import {
  Clock,
  CornerDownLeft,
  Download,
  Layers,
  Send,
  ShieldCheck,
  Wand2,
  Zap,
  type LucideIcon,
} from "lucide-react";
import type { SubscriptionType } from "../../../api";

export type StageId =
  | "trigger"
  | "source"
  | "schedule"
  | "aggregation"
  | "validation"
  | "transformation"
  | "delivery"
  | "response";

/**
 * Node title, icon, and the one-line purpose shown above the stage's
 * configuration.
 *
 * The icons are deliberately monochrome. Colour on this page means *status* —
 * a red node is broken, an amber one is degraded — so spending green and amber
 * on decoration would make a healthy pipeline look like a traffic light.
 */
export const STAGES: Record<StageId, { label: string; description: string; icon: LucideIcon }> = {
  trigger: { label: "Trigger", description: "What sets this subscription off.", icon: Zap },
  source: { label: "Source", description: "Where documents are pulled from.", icon: Download },
  schedule: {
    label: "Schedule",
    description: "When the source is checked for new documents.",
    icon: Clock,
  },
  aggregation: { label: "Aggregation", description: "What this subscription collects.", icon: Layers },
  validation: {
    label: "Validation",
    description: "Rejects bad documents before they enter the pipeline.",
    icon: ShieldCheck,
  },
  transformation: {
    label: "Transformation",
    description: "Reshapes the document before delivery.",
    icon: Wand2,
  },
  delivery: { label: "Delivery", description: "Where the document ends up.", icon: Send },
  response: {
    label: "Response",
    description: "What happens to what the delivery hands back.",
    icon: CornerDownLeft,
  },
};

/**
 * The pipeline, per subscription type — the shape of the rail and, with it,
 * which configuration the page offers at all.
 *
 * Two absences are deliberate:
 *
 * - **No Validation for BusGateway.** `RunValidator` is called from exactly one
 *   place, `Resources/Xchanges/Update.cs` (the partner-key API path). Bus-gateway
 *   subscriptions submit through `FilterService` and never reach it, so a
 *   validator saved here is stored and never executed. Offering the card would be
 *   offering a setting that does nothing.
 * - **No Transformation or Schedule for Aggregation.** Matches what the page has
 *   always done; aggregation configuration is still a placeholder pending its own
 *   pass, so its one card says so rather than pretending to be editable.
 */
export function stagesFor(type: SubscriptionType): StageId[] {
  switch (type) {
    case "Receiving":
      return ["source", "schedule", "transformation", "delivery", "response"];
    case "Aggregation":
      return ["aggregation", "delivery", "response"];
    case "BusGateway":
      return ["trigger", "transformation", "delivery", "response"];
    case "GatewayApiCall":
    case "ApiCall":
      return ["trigger", "validation", "transformation", "delivery", "response"];
    case "Internal":
      return ["trigger", "transformation", "delivery", "response"];
  }
}
