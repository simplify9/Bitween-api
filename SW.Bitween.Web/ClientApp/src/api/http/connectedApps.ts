import { get, post } from "./request";

/** An AI app an account signed in with OAuth to use /mcp. */
export interface ConnectedApp {
  clientId: string;
  name: string;
  /** Its session renews about once a sign-in session's length while the app is in use. */
  lastRenewedOn: string;
}

/** The AI apps connected as an account: the signed-in member's own when no id is given. */
export const listConnectedApps = (accountId?: string) =>
  get<ConnectedApp[]>(`/accounts/connectedapps${accountId === undefined ? "" : `?accountId=${accountId}`}`);

/** Ends an app's sessions, so it can't renew; what it holds now runs out within a session's length. */
export const disconnectApp = (clientId: string, accountId?: string) =>
  post<void>("/accounts/disconnectapp", { clientId, accountId: accountId === undefined ? undefined : Number(accountId) });
