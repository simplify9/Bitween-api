/** The server's keys for each gateway field it can refuse a save over: see ApiGateways/GatewayUrlName.cs. */
export const GATEWAY_ERRORS = {
  name: ["Name"],
  signature: [
    "GATEWAY_SIGNATURE_HEADER_INVALID",
    "GATEWAY_SIGNATURE_SCHEME_INVALID",
    "GATEWAY_SIGNATURE_SECRET_REQUIRED",
    "GATEWAY_SIGNATURE_TOLERANCE_INVALID",
  ],
  url: ["UrlName", "GATEWAY_URL_NAME_INVALID", "GATEWAY_URL_NAME_TAKEN"],
};
