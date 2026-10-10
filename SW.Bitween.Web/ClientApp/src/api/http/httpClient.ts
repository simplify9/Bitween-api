import type { ApiClient } from "../client";
import { adapterMethods } from "./adapters";
import { adapterDraftMethods } from "./adapterDrafts";
import { auditMethods } from "./audit";
import { categoryMethods } from "./categories";
import { dashboardMethods } from "./dashboard";
import { dataSourceMethods } from "./dataSources";
import { dataSourceStatementMethods } from "./dataSourceStatements";
import { documentMethods } from "./documents";
import { exchangeMethods } from "./exchanges";
import { gatewayMethods } from "./gateways";
import { globalValuesMethods } from "./globalValues";
import { subscriptionMethods } from "./subscriptions";
import { mapperMethods } from "./mappers";
import { notifierMethods } from "./notifiers";
import { partnerMethods } from "./partners";
import { queueHealthMethods } from "./queueHealth";
import { retryPolicyMethods } from "./retryPolicies";
import { sessionMethods } from "./session";
import { settingsMethods } from "./settings";
import { teamMethods } from "./team";
import { workGroupMethods } from "./workGroups";

/**
 * The client: every domain's methods merged. Typed as the whole ApiClient, so a method declared and
 * not implemented is a compile error rather than a call that fails at run time.
 */
export const httpClient: ApiClient = {
  ...sessionMethods,
  ...auditMethods,
  ...partnerMethods,
  ...documentMethods,
  ...globalValuesMethods,
  ...workGroupMethods,
  ...retryPolicyMethods,
  ...subscriptionMethods,
  ...categoryMethods,
  ...adapterMethods,
  ...adapterDraftMethods,
  ...gatewayMethods,
  ...exchangeMethods,
  ...queueHealthMethods,
  ...dataSourceStatementMethods,
  ...dashboardMethods,
  ...dataSourceMethods,
  ...mapperMethods,
  ...notifierMethods,
  ...teamMethods,
  ...settingsMethods,
};
