import { expect, type APIRequestContext, type APIResponse } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD, API, BASE_URL } from "./env";

/**
 * The administrator's view of the API, for the setup a spec needs but is not about, and for
 * checking afterwards that what the UI did really happened on the server.
 *
 * Signs in through the same endpoint the login page uses, so nothing here reaches past what a
 * real administrator could do. Everything a spec builds this way is named "Playwright …" so the
 * seed project's purge clears it if the spec dies before cleaning up.
 */
export class AdminApi {
  private readonly request: APIRequestContext;
  private readonly token: string;

  private constructor(request: APIRequestContext, token: string) {
    this.request = request;
    this.token = token;
  }

  static async signIn(request: APIRequestContext, email = ADMIN_EMAIL, password = ADMIN_PASSWORD) {
    const res = await request.post(`${API}/accounts/login`, { data: { Username: email, Password: password } });
    expect(res.ok(), `signing in to the API as ${email}: ${res.status()} ${await res.text()}`).toBe(true);
    return new AdminApi(request, (await res.json()).jwt as string);
  }

  private headers() {
    return { Authorization: `Bearer ${this.token}` };
  }

  async raw(method: "GET" | "POST" | "DELETE", path: string, data?: unknown): Promise<APIResponse> {
    return this.request.fetch(`${API}${path}`, {
      method,
      headers: this.headers(),
      ...(data === undefined ? {} : { data }),
    });
  }

  async call<T = unknown>(method: "GET" | "POST" | "DELETE", path: string, data?: unknown): Promise<T> {
    const res = await this.raw(method, path, data);
    if (!res.ok()) throw new Error(`${method} ${path} failed: ${res.status()} ${await res.text()}`);
    const text = await res.text();
    return (text ? JSON.parse(text) : null) as T;
  }

  get = <T = unknown>(path: string) => this.call<T>("GET", path);
  post = <T = unknown>(path: string, data: unknown = {}) => this.call<T>("POST", path, data);
  delete = <T = unknown>(path: string) => this.call<T>("DELETE", path);

  /** The id of the row with exactly this name. */
  async idOf(list: string, name: string): Promise<number> {
    const filter = encodeURIComponent(`Name:4:${name}`);
    const res = await this.get<{ result: { id: number; name: string }[] }>(
      `/${list}?filter=${filter}&page=0&size=100`,
    );
    const row = res.result.find((r) => r.name === name);
    if (!row) throw new Error(`no ${list} row named "${name}" — did the seed project run?`);
    return row.id;
  }

  async account(email: string) {
    const res = await this.get<{ result: { id: number; email: string; disabled?: boolean; roles: { id: number; name: string }[] | null }[] }>(
      `/accounts?limit=500`,
    );
    const row = res.result.find((a) => a.email.toLowerCase() === email.toLowerCase());
    if (!row) throw new Error(`no account ${email}`);
    return row;
  }

  createPartner(name: string) {
    return this.post<number>("/partners", { name, adapterProperties: {}, secretProperties: [], loginIdentity: null });
  }

  /** A gateway subscription that delivers nothing, so calling the gateway creates no failures. */
  createGatewaySubscription(name: string, documentId: number) {
    return this.post<number>("/subscriptions", {
      name,
      documentId,
      type: "GatewayApiCall",
      handlerId: null,
      handlerProperties: [],
      receiverProperties: [],
      validatorProperties: [],
      mapperProperties: [],
      documentFilter: [],
      inactive: false,
    });
  }

  /** An API gateway with one partner attached to a subscription that delivers nothing. */
  async gatewayFor(partnerId: number, documentId: number, stamp: string) {
    const urlName = `playwright-${stamp}`;
    const gatewayId = await this.post<number>("/apigateways", {
      name: `Playwright Gateway ${stamp}`,
      urlName,
      inactive: false,
    });
    const subscriptionId = await this.createGatewaySubscription(`Playwright Gateway Sub ${stamp}`, documentId);
    await this.post(`/apigateways/${gatewayId}/addpartner`, { partnerId, subscriptionId });
    return { gatewayId, urlName, subscriptionId };
  }
}

/** POSTs a document to an API gateway the way a partner would, and answers the status. */
export async function callGateway(
  request: APIRequestContext,
  urlName: string,
  headers: Record<string, string>,
): Promise<number> {
  const res = await request.post(`${BASE_URL}api/gateway/${urlName}/async`, {
    headers: { "Content-Type": "application/json", ...headers },
    data: JSON.stringify({ shipment: `e2e-gateway-${Date.now()}` }),
  });
  return res.status();
}

/** A unique, sortable suffix for names and url names. */
export const stamp = () => `${Date.now()}${Math.floor(Math.random() * 1000)}`;
