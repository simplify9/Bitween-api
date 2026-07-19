import { useNavigate, useSearchParams } from "react-router";
import { ArrowRight } from "lucide-react";
import { continueUrl, useReturnContext } from "../../lib/returnTo";
import { Button } from "./basics";

/**
 * Shown at the top of entity pages reached as a detour from a flow
 * (creating or editing a dependency). Continuing returns to the flow,
 * reporting what was just created via the `picked` param.
 */
export function ReturnBanner() {
  const ctx = useReturnContext();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  if (!ctx) return null;
  const picked = params.get("picked") ?? undefined;

  return (
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-crimson-200 bg-crimson-50/60 px-4 py-2.5">
      <p className="min-w-0 text-sm text-ink-700">
        In progress: <strong className="font-medium text-ink-900">{ctx.label}</strong>
      </p>
      <Button size="sm" variant="primary" onClick={() => navigate(continueUrl(ctx, picked))}>
        Continue <ArrowRight className="size-3.5" />
      </Button>
    </div>
  );
}
