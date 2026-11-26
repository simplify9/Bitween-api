import { createContext, useContext } from "react";
import { api, type GlobalValuesSetRow } from "../../api";
import type { ApiClient } from "../../api/client";

/**
 * What the mapping editor asks of the server beyond the rules themselves, and which of its admin
 * tools it shows. The app uses the admin API; the partner portal supplies its own, since a partner's
 * person is refused every admin endpoint, and turns off what isn't theirs: choosing a partner to
 * preview as, AI suggestions, and global values.
 */
export interface MapperEditorServices {
  previewMappingRules: ApiClient["previewMappingRules"];
  /** The property names of a partner, to suggest in a Partner rule. */
  partnerPropertyKeys: (partnerId: number) => Promise<string[]>;
  listValueSets: () => Promise<GlobalValuesSetRow[]>;
  /** False when the partner is fixed, as in the portal: no picker for whose values to preview with. */
  choosePartner: boolean;
  ai: boolean;
  /** False keeps the Global choice off new rules; a rule already reading one still shows it. */
  globals: boolean;
}

export const ADMIN_MAPPER_SERVICES: MapperEditorServices = {
  previewMappingRules: (input) => api.previewMappingRules(input),
  partnerPropertyKeys: async (partnerId) => Object.keys(await api.getPartnerAdapterProperties(partnerId)),
  listValueSets: () => api.listValueSets(),
  choosePartner: true,
  ai: true,
  globals: true,
};

export const MapperEditorServicesContext = createContext<MapperEditorServices>(ADMIN_MAPPER_SERVICES);

export const useMapperEditorServices = () => useContext(MapperEditorServicesContext);
