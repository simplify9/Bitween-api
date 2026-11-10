"use strict";
/**
 * Bitween from your own program, as a service account: search exchanges, read their files, retry and
 * export them, and set integrations up - partners, subscriptions, gateways and notification channels.
 * BitweenGateway sends to an API gateway as a partner. The JavaScript form of
 * SimplyWorks.Bitween.Client, with the same calls.
 *
 *   const { BitweenClient } = require("@simplyworks/bitween-client");
 *
 *   const bitween = new BitweenClient({ url: "https://bitween.example.com", token: process.env.BITWEEN_TOKEN });
 *   const failed = await bitween.exchanges.search({ status: "failed" });
 *
 * The token is traded for a session on the first call, and traded again whenever the session runs
 * out or is refused. Keep one client for the life of the program: calls made at once share it.
 */

const STATUS = { processing: 0, success: 1, badResponse: 2, failed: 3 };

/** What a Bitween refused, and why: `status`, and `errors` by code, such as ALREADY_RETRIED. */
class BitweenClientError extends Error {
  constructor(message, status, errors = {}) {
    super(message);
    this.name = "BitweenClientError";
    this.status = status;
    this.errors = errors;
    this.code = Object.keys(errors)[0] ?? null;
  }
}

/**
 * Validation answers as `{ CODE: [messages] }`, and problem details with `errors` in that shape.
 * Problem details without them say only the status, so they give no reasons.
 */
async function refusal(response) {
  const body = await response.text();
  const errors = {};
  try {
    const answer = JSON.parse(body);
    if (answer && typeof answer === "object" && !Array.isArray(answer)) {
      const reasons = answer.errors ?? ("status" in answer ? {} : answer);
      for (const [code, value] of Object.entries(reasons))
        errors[code] = Array.isArray(value) ? value.map(String) : value == null ? [] : [String(value)];
    }
  } catch {}
  const messages = Object.values(errors).flat().filter((m) => m.trim());
  const message = messages.length ? messages.join(" ")
    : response.status === 401 ? "Bitween refused the service token, or the session made with it."
    : response.status === 403 ? "The service account's roles don't have the permission this needs."
    : response.status === 404 ? "Bitween has no such thing."
    : `Bitween answered ${response.status}${body.trim() ? `: ${body.trim()}` : ""}`;
  return new BitweenClientError(message, response.status, errors);
}

const time = (t) => new Date(t).toISOString();

/** Filters, paging and the rest as a query string, every value escaped. */
const queryString = (filters, more = {}) =>
  [...filters.map((f) => `filter=${encodeURIComponent(f)}`),
    ...Object.entries(more).map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`)].join("&");

/** An exchange query's filters as Bitween's search takes them, each one `Field:Rule:Value`. */
function exchangeFilters(query = {}) {
  const filters = [];
  // Rule 1 is "is"; 9 is "is any of", with its values typed and joined.
  const oneOf = (field, values) => {
    if (!values?.length) return;
    filters.push(values.length === 1 ? `${field}:1:${values[0]}` : `${field}:9:numeric|${values.join("|")}`);
  };
  if (query.status != null) {
    if (!Object.hasOwn(STATUS, query.status)) throw new TypeError(`status is one of ${Object.keys(STATUS).join(", ")}.`);
    filters.push(`StatusFilter:1:${STATUS[query.status]}`);
  }
  oneOf("PartnerId", query.partnerIds);
  oneOf("SubscriptionId", query.subscriptionIds);
  oneOf("DocumentId", query.informationTypeIds);
  if (query.ids?.length) filters.push(`Id:4:text|${query.ids.join("|")}`);
  if (query.retryFor?.trim()) filters.push(`RetryFor:1:${query.retryFor.trim()}`);
  if (query.correlationId?.trim()) filters.push(`CorrelationId:1:${query.correlationId.trim()}`);
  if (query.latestOnly) filters.push("LatestOnly:1:true");
  if (query.errorContains?.trim()) filters.push(`ErrorContains:4:${query.errorContains.trim()}`);
  for (const [key, text] of Object.entries(query.promotedProperties ?? {}))
    filters.push(text?.trim() ? `PromotedProperty:4:${key}:${text.trim()}` : `PromotedProperty:1:${key}`);
  // Rules 6 and 8, at or after and at or before: the range rule, 21, can't be sent a time Postgres
  // will compare (the Exchanges page found the same).
  if (query.from != null) filters.push(`StartedOn:6:${time(query.from)}`);
  if (query.to != null) filters.push(`StartedOn:8:${time(query.to)}`);
  filters.push(...(query.filters ?? []));
  return filters;
}

/** A selection as the bulk endpoints take it: `{ ids }`, or `{ matching, except }`. */
function selectionBody(selection) {
  if (!selection?.ids === !selection?.matching) throw new TypeError("A selection is { ids } or { matching, except }.");
  if (selection.ids) return { ids: [...selection.ids] };
  // An empty list adds no filter, so it would widen the selection rather than narrow it.
  for (const key of ["ids", "partnerIds", "subscriptionIds", "informationTypeIds"])
    if (selection.matching[key]?.length === 0) throw new TypeError(`matching.${key} is empty: it would widen the selection, not narrow it.`);
  const filters = exchangeFilters(selection.matching);
  // An empty filter reads as no filter, and the selection would fall back to its ids, which are
  // none. Every attempt, not only the newest, matches everything without narrowing it.
  if (!filters.length) filters.push("LatestOnly:1:false");
  return { filter: queryString(filters), excludeIds: selection.except ? [...selection.except] : null };
}

class Exchanges {
  #request;

  constructor(request) {
    this.#request = request;
  }

  /** A page of the exchanges the query matches, newest first, and how many match. */
  async search(query = {}, { page = 0, size = 25, count = true } = {}) {
    const paging = count ? { page, size } : { page, size, count: false };
    return (await this.#request("GET", `api/xchanges?${queryString(exchangeFilters(query), paging)}`)).json();
  }

  /** One exchange, or null when there is none by that id. */
  async get(id) {
    // The id search also finds the exchange's retries, which are newer, and the exchanges it rolled
    // up, which are older. One retry is all an exchange gets now, but chains from before that rule
    // can have several, so it is read a page at a time until the exchange is found or the rows end.
    for (let page = 0; ; page++) {
      const found = await this.search({ ids: [id] }, { page, size: 25, count: false });
      const exchange = found.result.find((x) => x.id === id);
      if (exchange || found.result.length < 25) return exchange ?? null;
    }
  }

  /** One of an exchange's files, by the key its row gives: inputKey, outputKey or responseKey. */
  async readFile(key) {
    const file = await (await this.#request("GET", `api/bitweendocs?documentKey=${encodeURIComponent(key)}`)).json();
    return file.data;
  }

  /** Every attempt in the retry chain the exchange belongs to, oldest first. */
  async retryTree(id) {
    return (await this.#request("GET", `api/xchanges/retrytree?id=${encodeURIComponent(id)}`)).json();
  }

  /**
   * Runs an exchange again from its input file, and resolves to the new attempt's id: null when it
   * can't look that up, because the account may retry but not view exchanges or the lookup failed. An exchange is retried at most
   * once: one that has been is refused with ALREADY_RETRIED. One with an automatic retry booked is
   * refused with AUTO_RETRY_SCHEDULED; use scheduledRetries.runNow instead.
   */
  async retry(id, { reset = false, reason = "Manual retry" } = {}) {
    await this.#request("POST", `api/xchanges/${encodeURIComponent(id)}/retry`, { reason, reset });
    // Answered with nothing: the new attempt is the newest that says it retries this. Looking it up
    // needs exchanges.view, and the retry is made by then, so a failed lookup mustn't read as the retry's.
    try {
      const retry = await this.search({ retryFor: id }, { page: 0, size: 1, count: false });
      return retry.result[0]?.id ?? null;
    } catch {
      return null;
    }
  }

  /** What bulkRetry would do with the same selection, changing nothing. */
  async previewBulkRetry(selection, { reset = false } = {}) {
    const body = { ...selectionBody(selection), reason: "Bulk retry", reset };
    return (await this.#request("POST", "api/xchanges/bulkretrypreview", body)).json();
  }

  /**
   * Retries a selection, each chain once from its newest attempt, and says what it did. At most
   * `limit` (500) at a time: past that the plan comes back with `overLimit` set and nothing done.
   * To retry more, retry the preview's `targets` as `{ ids }`, that many at a time.
   */
  async bulkRetry(selection, { reset = false, reason = "Bulk retry" } = {}) {
    const body = { ...selectionBody(selection), reason, reset };
    return (await this.#request("POST", "api/xchanges/bulkretry", body)).json();
  }

  /** How many exchanges export would take, and how many would come without files. Changes nothing. */
  async checkExport(selection) {
    return (await this.#request("POST", "api/xchanges/export/check", selectionBody(selection))).json();
  }

  /**
   * The selection's files as one zip, a folder for each exchange, as a stream read as it downloads.
   * At most 500 exchanges; more is refused with TOO_MANY.
   */
  async export(selection) {
    return (await this.#request("POST", "api/xchanges/export", selectionBody(selection))).body;
  }
}

class ScheduledRetries {
  #request;

  constructor(request) {
    this.#request = request;
  }

  /** A page of the automatic retries waiting to run, soonest first, and how many there are. */
  async search(query = {}, { page = 0, size = 25 } = {}) {
    const filters = [];
    if (query.subscriptionId != null) filters.push(`SubscriptionId:1:${query.subscriptionId}`);
    if (query.informationTypeId != null) filters.push(`DocumentId:1:${query.informationTypeId}`);
    if (query.errorContains?.trim()) filters.push(`Exception:4:${query.errorContains.trim()}`);
    if (query.from != null) filters.push(`On:6:${time(query.from)}`);
    if (query.to != null) filters.push(`On:8:${time(query.to)}`);
    return (await this.#request("GET", `api/delayedretries?${queryString(filters, { page, size })}`)).json();
  }

  /** Runs the automatic retry booked for the exchange now, rather than when it's due. */
  async runNow(id) {
    await this.#request("POST", `api/delayedretries/${encodeURIComponent(id)}/runnow`, {});
  }
}


const SUBSCRIPTION_TYPE = { internal: 1, apiCall: 2, receiving: 4, aggregation: 8, gatewayApiCall: 16, busGateway: 32, response: 64 };

/**
 * A list's filters: names containing `nameContains`, matched as the database matches it
 * (case-sensitively on PostgreSQL), plus raw `Field:Rule:Value` filters.
 */
function listFilters(query = {}) {
  const filters = [];
  if (query.nameContains?.trim()) filters.push(`Name:4:${query.nameContains.trim()}`);
  filters.push(...(query.filters ?? []));
  return filters;
}

function subscriptionFilters(query = {}) {
  const filters = listFilters(query);
  if (query.type != null) {
    if (!Object.hasOwn(SUBSCRIPTION_TYPE, query.type)) throw new TypeError(`type is one of ${Object.keys(SUBSCRIPTION_TYPE).join(", ")}.`);
    filters.push(`Type:1:${SUBSCRIPTION_TYPE[query.type]}`);
  }
  if (query.informationTypeId != null) filters.push(`DocumentId:1:${query.informationTypeId}`);
  if (query.partnerId != null) filters.push(`PartnerId:1:${query.partnerId}`);
  return filters;
}

// Module-private: what the areas share, kept off their public surface.
const REQUEST = Symbol("request");
const POST = Symbol("post");

/** One resource's calls: list, read, create, save, delete, on `api/{resource}`. */
class Area {
  #request;
  #resource;
  #filters;

  constructor(request, resource, filters = listFilters) {
    this.#request = request;
    this.#resource = resource;
    this.#filters = filters;
  }

  get [REQUEST]() { return this.#request; }

  /** A page of them, and how many match. */
  async search(query = {}, { page = 0, size = 25 } = {}) {
    return (await this.#request("GET", `api/${this.#resource}?${queryString(this.#filters(query), { page, size })}`)).json();
  }

  /** One, or null when there is none by that id. */
  async get(id) {
    try {
      const text = await (await this.#request("GET", `api/${this.#resource}/${encodeURIComponent(id)}`)).text();
      return text.trim() ? JSON.parse(text) : null;
    } catch (e) {
      if (e instanceof BitweenClientError && e.status === 404) return null;
      throw e;
    }
  }

  /** Creates one, and resolves to its id. */
  async create(body) {
    return (await this.#request("POST", `api/${this.#resource}`, body)).json();
  }

  /** Saves it, replacing what it had. */
  async update(id, body) {
    await this.#request("POST", `api/${this.#resource}/${encodeURIComponent(id)}`, body);
  }

  async delete(id) {
    await this.#request("DELETE", `api/${this.#resource}/${encodeURIComponent(id)}`);
  }

  async [POST](path, body) {
    return this.#request("POST", `api/${this.#resource}/${path}`, body);
  }
}

/**
 * Partners. Settings named in `secretProperties` come back as `__private__`, and sending that back
 * keeps the stored value; keys come back masked, and a masked key sent back keeps the key. So a
 * partner read with `get` can be changed and saved with `update`.
 */
class Partners extends Area {
  constructor(request) { super(request, "partners"); }

  /** A new random key to give a partner. Nothing is stored until it is saved on one. */
  async newKey() {
    const text = (await (await this[REQUEST]("GET", "api/partners/generatekey")).text()).trim();
    return text.startsWith('"') ? JSON.parse(text) : text;
  }
}

/**
 * Subscriptions, created inactive unless `inactive: false`. Private adapter settings come back as
 * `__private__`, and sending that back keeps the stored value. `update` replaces the whole
 * configuration, except `notifications`, which null or absent leaves as they are.
 */
class Subscriptions extends Area {
  constructor(request) { super(request, "subscriptions", subscriptionFilters); }

  /** Switches it on, as the page's Enable does: false when it was on already. */
  activate(id) { return this.#setInactive(id, false); }

  /** Switches it off: false when it was off already. */
  deactivate(id) { return this.#setInactive(id, true); }

  /** Pauses it: false when it was paused already. The API toggles, so it's read first. */
  pause(id) { return this.#setPaused(id, true); }

  /** Resumes it: false when it wasn't paused. */
  resume(id) { return this.#setPaused(id, false); }

  async #read(id) {
    const subscription = await this.get(id);
    if (!subscription) throw new BitweenClientError(`Bitween has no subscription ${id}.`, 404);
    return subscription;
  }

  async #setInactive(id, inactive) {
    const subscription = await this.#read(id);
    if (subscription.inactive === inactive) return false;
    await this.update(id, { ...subscription, inactive });
    return true;
  }

  async #setPaused(id, paused) {
    const subscription = await this.#read(id);
    if ((subscription.pausedOn != null) === paused) return false;
    await this[POST](`${encodeURIComponent(id)}/pause`, {});
    return true;
  }
}

/** Information types (documents, in the API): read only. */
class InformationTypes {
  #area;
  constructor(request) { this.#area = new Area(request, "documents"); }
  search(query, paging) { return this.#area.search(query, paging); }
  get(id) { return this.#area.get(id); }
}

/** API gateways. A partner attached to one runs one subscription through it. */
class ApiGateways extends Area {
  constructor(request) { super(request, "apigateways"); }

  /** Attaches a partner: `{ partnerId, subscriptionId }`, or `{ partnerId, newIntegration }`. */
  async attachPartner(id, attachment) { await this[POST](`${encodeURIComponent(id)}/addpartner`, attachment); }

  /** Points an attached partner at another subscription. */
  async updatePartner(id, partnerId, subscriptionId) {
    await this[POST](`${encodeURIComponent(id)}/updatepartner`, { partnerId, subscriptionId });
  }

  async detachPartner(id, partnerId) { await this[POST](`${encodeURIComponent(id)}/removepartner`, { partnerId }); }
}

/**
 * Bus gateways. A route sends the messages matching its `matchExpression` - `{ type: "one_of", path,
 * values }`, `not_one_of` the same, or `and` / `or` with `left` and `right` - to a subscription, as a partner.
 */
class BusGateways extends Area {
  constructor(request) { super(request, "busgateways"); }

  /** Adds a route, `{ subscriptionId or newIntegration, partnerId, matchExpression }`, and resolves to its id. */
  async addRoute(id, route) { return (await this[POST](`${encodeURIComponent(id)}/addroute`, route)).json(); }

  /** Replaces route `routeId`'s subscription, partner and expression. */
  async updateRoute(id, route) { await this[POST](`${encodeURIComponent(id)}/updateroute`, route); }

  async removeRoute(id, routeId) { await this[POST](`${encodeURIComponent(id)}/removeroute`, { routeId }); }
}

/** Notification channels. Secret settings come back as `__private__`; sending that back keeps them. */
class NotificationChannels extends Area {
  constructor(request) { super(request, "notificationchannels"); }

  /** Sends one made-up notification through it as saved: `{ sent, error }`. */
  async test(id) { return (await this[POST](`${encodeURIComponent(id)}/test`, {})).json(); }
}

/** The body as an exchange id, which the gateway answers as plain text; null when it is anything else. */
function bareId(body) {
  const text = body.trim();
  if (!text || text.length > 64) return null;
  if (text.startsWith('"')) {
    try { return JSON.parse(text); } catch { return null; }
  }
  return /^[A-Za-z0-9_-]+$/.test(text) ? text : null;
}

function parsed(body) {
  try {
    const value = JSON.parse(body);
    return value && typeof value === "object" && !Array.isArray(value) ? value : null;
  } catch {
    return null;
  }
}

/**
 * Sends to API gateways as a partner, with one of its keys: what a partner's own program does.
 * Refusals - an unknown gateway, a key it doesn't take, a gateway switched off, a payload the schema
 * rejects - throw BitweenClientError; how a delivery went is the answer's `outcome`.
 */
class BitweenGateway {
  #url;
  #key;
  #keyHeader;
  #fetch;

  /**
   * @param {object} options
   * @param {string} options.url The Bitween's address.
   * @param {string} options.partnerKey One of the partner's keys.
   * @param {string} [options.keyHeader] The header the gateway reads the key from; partnerkey always works.
   * @param {typeof fetch} [options.fetch]
   */
  constructor({ url, partnerKey, keyHeader = "partnerkey", fetch: send = globalThis.fetch } = {}) {
    if (!url) throw new TypeError("url is required: the Bitween's address.");
    if (!partnerKey?.trim()) throw new TypeError("partnerKey is required: one of the partner's keys.");
    this.#url = url.replace(/\/+$/, "") + "/";
    this.#key = partnerKey.trim();
    this.#keyHeader = keyHeader.trim();
    this.#fetch = send;
  }

  /**
   * Sends the payload and resolves once Bitween has stored it: `{ exchangeId, replayed, duplicateOf }`.
   * With `idempotencyKey`, a repeat is answered with the first call's exchange.
   */
  async send(urlName, payload, { idempotencyKey, contentType = "application/json" } = {}) {
    const response = await this.#post(urlName, "async", payload, { idempotencyKey, contentType });
    const body = await response.text();
    if (response.status !== 202 && response.status !== 200) throw await this.#refusal(response, body);
    const exchangeId = response.headers.get(EXCHANGE_ID_HEADER) ?? bareId(body);
    if (!exchangeId) throw new BitweenClientError("Bitween accepted the call without naming its exchange.", response.status);
    return { exchangeId, ...this.#marks(response) };
  }

  /**
   * Sends the payload and waits for the delivery, at most `waitSeconds` (Bitween's default when absent).
   * Resolves to `{ outcome, status, exchangeId, body, contentType, error, nextAttemptOn, replayed,
   * duplicateOf }`, `outcome` one of delivered, badResponse, failed, retrying, held and stillRunning.
   */
  async sendAndWait(urlName, payload, { waitSeconds, idempotencyKey, contentType = "application/json" } = {}) {
    if (waitSeconds != null && !(waitSeconds >= 1)) throw new RangeError("A wait is at least a second.");
    const response = await this.#post(urlName, "sync", payload, { idempotencyKey, contentType, waitSeconds });
    const body = await response.text();
    const header = response.headers.get(EXCHANGE_ID_HEADER);
    const answer = (outcome, more = {}) => ({
      outcome, status: response.status, exchangeId: header ?? more.exchangeId ?? null,
      body: more.body ?? null, contentType: more.body == null ? null : response.headers.get("content-type"),
      error: more.error ?? null, nextAttemptOn: more.nextAttemptOn ?? null, ...this.#marks(response),
    });
    const object = parsed(body);
    switch (response.status) {
      case 200:
        // Delivered with no response: answered with the exchange id alone.
        return header && bareId(body) === header ? answer("delivered") : answer("delivered", { body });
      case 202:
        if (object?.status === "retrying") return answer("retrying", object);
        if (object?.status === "held") return answer("held", object);
        return answer("stillRunning", { exchangeId: bareId(body) });
      case 400:
        if (object?.error === "SCHEMA_MISMATCH") throw await this.#refusal(response, body);
        // A Legacy gateway answers every failure with an empty 400.
        return body ? answer("badResponse", { body }) : answer("failed");
      case 422:
      case 502:
        if (object?.status === "failed") return answer("failed", object);
        throw await this.#refusal(response, body);
      default:
        throw await this.#refusal(response, body);
    }
  }

  #post(urlName, mode, payload, { idempotencyKey, contentType, waitSeconds }) {
    if (!urlName?.trim()) throw new TypeError("urlName is required: the gateway's URL name.");
    if (payload == null) throw new TypeError("payload is required.");
    const path = urlName.replace(/^\/+|\/+$/g, "").split("/").map(encodeURIComponent).join("/");
    const headers = { "Content-Type": contentType, [this.#keyHeader]: this.#key };
    if (idempotencyKey?.trim()) headers["Idempotency-Key"] = idempotencyKey.trim();
    if (waitSeconds != null) headers["Wait-Period"] = String(waitSeconds);
    const body = typeof payload === "string" ? payload : JSON.stringify(payload);
    return this.#fetch(`${this.#url}api/gateway/${path}/${mode}`, { method: "POST", headers, body });
  }

  #marks(response) {
    return { replayed: response.headers.get("idempotent-replay") === "true", duplicateOf: response.headers.get("bitween-duplicate-of") };
  }

  async #refusal(response, body) {
    const object = parsed(body);
    if (object?.error === "SCHEMA_MISMATCH") {
      const messages = Array.isArray(object.errors) ? object.errors.map(String) : [];
      return new BitweenClientError(messages.join(" ") || "The payload doesn't fit its information type's schema.", 400, { SCHEMA_MISMATCH: messages });
    }
    if (response.status === 404) return new BitweenClientError("Bitween has no such gateway, or the subscription it runs is switched off.", 404);
    if (response.status === 401) return new BitweenClientError("The gateway didn't take the key, or the partner isn't attached to it.", 401);
    return refusal(new Response(body, { status: response.status }));
  }
}

const EXCHANGE_ID_HEADER = "bitween-exchange-id";

class BitweenClient {
  #url;
  #token;
  #fetch;
  #held = null;
  #signingIn = null;

  /**
   * @param {object} options
   * @param {string} options.url The Bitween's address, such as https://bitween.example.com.
   * @param {string} options.token A service account's token, starting bwt_.
   * @param {typeof fetch} [options.fetch] The fetch to send with; the global one unless given.
   */
  constructor({ url, token, fetch: send = globalThis.fetch } = {}) {
    if (!url) throw new TypeError("url is required: the Bitween's address.");
    if (!token?.trim()) throw new TypeError("token is required: a service account's token, starting bwt_.");
    this.#url = url.replace(/\/+$/, "") + "/";
    this.#token = token.trim();
    this.#fetch = send;
    const request = (method, path, body) => this.#request(method, path, body);
    /** Search exchanges, read their files, and retry or export them. */
    this.exchanges = new Exchanges(request);
    /** The automatic retries waiting to run, and running one now. */
    this.scheduledRetries = new ScheduledRetries(request);
    /** Partners: who an integration runs for, their settings and secrets, and their keys. */
    this.partners = new Partners(request);
    /** Subscriptions: the integrations themselves, switched on and off and paused. */
    this.subscriptions = new Subscriptions(request);
    /** Information types (documents, in the API). Read only. */
    this.informationTypes = new InformationTypes(request);
    /** API gateways, and which partner runs which subscription through each. */
    this.apiGateways = new ApiGateways(request);
    /** Bus gateways, and the routes that send their messages to subscriptions. */
    this.busGateways = new BusGateways(request);
    /** Notification channels: where results and alerts are sent. */
    this.notificationChannels = new NotificationChannels(request);
  }

  async #request(method, path, body) {
    let used = await this.#session(null);
    let response = await this.#send(method, path, body, used);
    if (response.status === 401) {
      // Ended before its time, as by the account's roles changing: sign in again once and try again.
      await response.body?.cancel();
      used = await this.#session(used);
      response = await this.#send(method, path, body, used);
    }
    if (!response.ok) throw await refusal(response);
    return response;
  }

  #send(method, path, body, session) {
    const headers = { Authorization: `Bearer ${session.jwt}` };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    return this.#fetch(this.#url + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  }

  /**
   * The session to send: the one held, unless it is about to end or is `refused`, when the token is
   * traded for a new one. Calls that find it so together share one trade and its answer, a refusal
   * included: a bad token sent once a call would soon lock the address out.
   */
  async #session(refused) {
    const held = this.#held;
    if (held && held !== refused && Date.now() < held.renewAfter) return held;
    this.#signingIn ??= this.#signIn().finally(() => { this.#signingIn = null; });
    return this.#signingIn;
  }

  async #signIn() {
    const response = await this.#fetch(this.#url + "api/accounts/servicetoken", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token: this.#token }),
    });
    if (!response.ok) throw await refusal(response);
    const answer = await response.json();
    if (!answer?.jwt) throw new BitweenClientError("Bitween answered the sign-in without a session; is this a Bitween?", response.status);
    // Renewed a minute early, so no call goes out on a session that ends on its way; halfway through
    // one shorter than two minutes, so it is still used.
    const lifetime = answer.expiresIn ?? 0;
    this.#held = { jwt: answer.jwt, renewAfter: Date.now() + (lifetime - Math.min(60, lifetime / 2)) * 1000 };
    return this.#held;
  }
}

module.exports = { BitweenClient, BitweenGateway, BitweenClientError };
