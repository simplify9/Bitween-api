import type { GatewayAuthentication } from "../../api";

/** Checked here too, so a field says what's wrong before a save is tried; the server refuses the same. */
export const signatureProblems = (
  a: GatewayAuthentication,
): {
  signatureHeader?: string;
  signatureSecretProperty?: string;
  signatureToleranceSeconds?: string;
} => {
  if (a.method !== "Signature") return {};
  const header = a.signatureHeader.trim();
  const tolerance = a.signatureToleranceSeconds;
  return {
    signatureHeader: !header
      ? "Required: the header the signature comes in."
      : !/^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,100}$/.test(header)
        ? "Letters, digits and - or _ only, with no spaces."
        : undefined,
    signatureSecretProperty: !a.signatureSecretProperty.trim()
      ? "Required: the partner property holding the shared secret."
      : a.signatureSecretProperty.trim().length > 200
        ? "At most 200 characters."
        : undefined,
    signatureToleranceSeconds:
      tolerance != null && (tolerance < 1 || tolerance > 86400)
        ? "Between 1 second and a day."
        : undefined,
  };
};
