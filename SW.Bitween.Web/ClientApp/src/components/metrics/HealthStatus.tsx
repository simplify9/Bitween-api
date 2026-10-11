import type { HealthStatus } from "../../api/metricsTypes";
import { HEALTH } from "./health";

export function HealthStatusBadge({ status }: { status: HealthStatus }) {
  const h = HEALTH[status];
  return (
    <span
      title={h.explain}
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[12px] font-medium whitespace-nowrap ${h.className}`}
    >
      <h.icon className="size-3.5" aria-hidden />
      {h.label}
    </span>
  );
}
