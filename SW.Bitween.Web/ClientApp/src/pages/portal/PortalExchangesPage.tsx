import { ExchangesPage } from "../exchanges/ExchangesPage";
import { PartnerExchangeSource } from "../exchanges/exchangeSource";

/**
 * A partner's people's Exchanges page: Bitween's own, reading their exchanges through the portal's
 * API, without our tools and the links into our pages.
 */
export function PortalExchangesPage() {
  return (
    <PartnerExchangeSource>
      <ExchangesPage />
    </PartnerExchangeSource>
  );
}
