/** The server's keys for each gateway field it can refuse a save over: see ApiGateways/GatewayUrlName.cs. */
export const GATEWAY_ERRORS = {
  name: ["Name"],
  url: ["UrlName", "GATEWAY_URL_NAME_INVALID", "GATEWAY_URL_NAME_TAKEN"],
};
