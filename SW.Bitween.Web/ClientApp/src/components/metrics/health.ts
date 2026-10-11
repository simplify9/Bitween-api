import { BellOff, CircleCheck, CircleMinus, CirclePause, CircleX, TriangleAlert, type LucideIcon } from "lucide-react";
import type { HealthStatus } from "../../api/metricsTypes";

/** Each status with its words and icon: color is never the only way to tell them apart. */
export const HEALTH: Record<HealthStatus, { label: string; icon: LucideIcon; className: string; explain: string }> = {
  healthy: {
    label: "Healthy",
    icon: CircleCheck,
    className: "bg-ok-50 text-ok-800",
    explain: "99% or more succeeded.",
  },
  degraded: {
    label: "Degraded",
    icon: TriangleAlert,
    className: "bg-warn-100 text-warn-700",
    explain: "Between 90% and 99% succeeded.",
  },
  failing: {
    label: "Failing",
    icon: CircleX,
    className: "bg-danger-50 text-danger-800",
    explain: "Fewer than 90% succeeded.",
  },
  silent: {
    label: "Gone quiet",
    icon: BellOff,
    className: "bg-warn-100 text-warn-700",
    explain: "Usually has traffic, and has had none for well over its usual gap.",
  },
  idle: {
    label: "No traffic",
    icon: CircleMinus,
    className: "bg-ink-100 text-ink-700",
    explain: "Nothing went through it in the window.",
  },
  paused: {
    label: "Paused",
    icon: CirclePause,
    className: "bg-ink-100 text-ink-700",
    explain: "Holding incoming work until resumed.",
  },
  inactive: { label: "Off", icon: CirclePause, className: "bg-ink-100 text-ink-600", explain: "Switched off." },
};
