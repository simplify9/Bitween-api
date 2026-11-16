import { get, post } from "./request";

/** An AI app asking to sign in, as Bitween identified it. */
export interface OAuthClientInfo {
  name: string;
  /** Where approving sends the member back to, host and port. */
  redirectHost: string;
  /** Every address the app registered is on the member's own computer. */
  onlyThisComputer: boolean;
}

/** Who's asking to sign in with OAuth, checked against the address it wants to send the member back to. */
export const describeOAuthClient = (clientId: string, redirectUri: string) =>
  get<OAuthClientInfo>(
    `/oauth/client?client_id=${encodeURIComponent(clientId)}&redirect_uri=${encodeURIComponent(redirectUri)}`,
  );

export interface OAuthGrant {
  clientId: string;
  redirectUri: string;
  codeChallenge: string;
  codeChallengeMethod: string;
  resource: string | null;
  state: string | null;
  approve: boolean;
}

/**
 * The member's answer: the address to send them back to, built by the server so only one the app
 * registered is used - with a one-time code when they approved, or access_denied when they didn't.
 */
export const answerOAuthSignIn = (grant: OAuthGrant) => post<{ redirectTo: string }>("/oauth/grant", grant);
