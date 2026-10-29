import { post } from "./request";

/**
 * Signs the bitween CLI in as the member signed in here: a one-time code for the CLI's PKCE
 * challenge, which only the CLI that made the challenge can redeem.
 */
export const grantCliSignIn = (codeChallenge: string) =>
  post<{ code: string }>("/accounts/cligrant", { codeChallenge });
