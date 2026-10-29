import { Link } from "react-router";
import type { BusGatewayRoute, SubscriptionRow } from "../../../api";
import { HealthBadge, SubscriptionStatusBadges } from "../../../components/config/shared";
import { ExceptionLine } from "../../../components/ui/Exception";
import { CardList } from "../../../components/ui/RowCard";
import { WiderScreenNote } from "../../../components/ui/WiderScreenNote";
import { subscriptionPath } from "../../../lib/subscriptionPaths";
import { conditionText } from "./model";

/**
 * The gateway on a phone: each route as a card, in place of the canvas.
 *
 * At 390px the canvas showed an empty grid with the route node off to the right. What someone
 * checks on a phone is whether each route is healthy, why one is failing, and a way to the
 * subscription behind it to pause it or look closer; the gateway's own activate switch stays in
 * the toolbar above. Building a route waits for a wider screen.
 */
export function RouteCards({
  routes,
  rowsById,
}: {
  routes: BusGatewayRoute[];
  rowsById: Map<number, SubscriptionRow>;
}) {
  return (
    <div className="space-y-3 bg-canvas p-4">
      <WiderScreenNote what="The route canvas" />
      {routes.length === 0 ? (
        <p className="text-[13px] text-ink-500">No routes yet - every message reaching this gateway is ignored.</p>
      ) : (
        <CardList
          rows={routes}
          rowKey={(r) => r.id}
          label="Routes"
          card={(r) => {
            const row = rowsById.get(r.subscriptionId);
            return {
              title: r.subscriptionName || `#${r.subscriptionId}`,
              status: row && (
                <>
                  <SubscriptionStatusBadges enabled={row.enabled} paused={row.paused} />
                  <HealthBadge isRunning={row.isRunning} consecutiveFailures={row.consecutiveFailures} />
                </>
              ),
              subtitle: `${conditionText(r.matchExpression)} · ${r.partnerName ?? "Any partner"}`,
              footer: (
                <div className="flex flex-col gap-2">
                  {row?.lastException && (
                    <ExceptionLine text={row.lastException} className="text-[12px] text-danger-700" />
                  )}
                  <Link to={subscriptionPath(r.subscriptionId, row?.type)} className="link self-start py-1 text-[13px]">
                    Open subscription
                  </Link>
                </div>
              ),
            };
          }}
        />
      )}
    </div>
  );
}
