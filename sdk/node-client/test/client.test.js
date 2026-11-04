"use strict";
const test = require("node:test");
const assert = require("node:assert");
const http = require("node:http");
const { BitweenClient, BitweenClientError } = require("../src");

/**
 * A stand-in Bitween: answers sign-ins with a new session each time, and everything else from
 * `answer`, keeping each request it was sent. The client's calls against a real one are tested in
 * SW.Bitween.HttpTests, through the .NET client that sends the same requests.
 */
async function fakeBitween(answer, { expiresIn = 3600 } = {}) {
  const seen = { signIns: 0, requests: [] };
  const server = http.createServer(async (req, res) => {
    let body = "";
    for await (const chunk of req) body += chunk;
    const url = new URL(req.url, "http://localhost");
    if (url.pathname === "/api/accounts/servicetoken") {
      seen.signIns++;
      const { token } = JSON.parse(body);
      res.writeHead(token === "bwt_good" ? 200 : 400, { "Content-Type": "application/json" });
      return res.end(JSON.stringify(token === "bwt_good"
        ? { jwt: `session-${seen.signIns}`, expiresIn }
        : { SWException: ["This token isn't valid: it's unknown, expired or revoked, or its account is disabled."] }));
    }
    const request = { method: req.method, path: url.pathname, filters: url.searchParams.getAll("filter"),
      query: Object.fromEntries(url.searchParams), auth: req.headers.authorization, body: body ? JSON.parse(body) : undefined };
    seen.requests.push(request);
    const [status, reply, type = "application/json"] = answer(request) ?? [200, null];
    res.writeHead(status, { "Content-Type": type });
    res.end(reply == null ? "" : typeof reply === "string" || Buffer.isBuffer(reply) ? reply : JSON.stringify(reply));
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  test.after(() => server.close());
  const client = (token = "bwt_good") => new BitweenClient({ url: `http://127.0.0.1:${server.address().port}/`, token });
  return { seen, client };
}

const page = (...ids) => ({ result: ids.map((id) => ({ id })), totalCount: ids.length });

test("searches with the Exchanges page's filters, signed in once for calls made at once", async () => {
  const { seen, client } = await fakeBitween(() => [200, page("a")]);
  const bitween = client();
  await Promise.all([
    bitween.exchanges.search({
      status: "failed", partnerIds: [7], subscriptionIds: [1, 2], latestOnly: true, errorContains: " timeout ",
      promotedProperties: { order: "SO-1", store: null }, from: new Date("2026-11-01T00:00:00Z"), filters: ["IsRetry:1:true"],
    }, { page: 2, size: 10, count: false }),
    bitween.exchanges.search(),
  ]);

  assert.strictEqual(seen.signIns, 1);
  const [search, plain] = seen.requests;
  assert.strictEqual(search.path, "/api/xchanges");
  assert.strictEqual(search.auth, "Bearer session-1");
  assert.deepStrictEqual(search.filters, [
    "StatusFilter:1:3", "PartnerId:1:7", "SubscriptionId:9:numeric|1|2", "LatestOnly:1:true", "ErrorContains:4:timeout",
    "PromotedProperty:4:order:SO-1", "PromotedProperty:1:store", "StartedOn:6:2026-11-01T00:00:00.000Z", "IsRetry:1:true",
  ]);
  assert.deepStrictEqual([search.query.page, search.query.size, search.query.count], ["2", "10", "false"]);
  assert.deepStrictEqual([plain.filters, plain.query.page, plain.query.size, plain.query.count], [[], "0", "25", undefined]);
  await assert.rejects(bitween.exchanges.search({ status: "broken" }), TypeError);
  await assert.rejects(bitween.exchanges.search({ status: "toString" }), TypeError);
});

test("signs in again once when the session is refused", async () => {
  const { seen, client } = await fakeBitween((r) => r.auth === "Bearer session-1" ? [401, null] : [200, page()]);
  await client().exchanges.search();
  assert.strictEqual(seen.signIns, 2);
  assert.deepStrictEqual(seen.requests.map((r) => r.auth), ["Bearer session-1", "Bearer session-2"]);
});

test("shares one refused sign-in between calls made at once, and keeps a short session", async () => {
  // Sent once a call, the fifth refusal would lock the address out, a good token from it with them.
  const { seen, client } = await fakeBitween(() => [200, page()]);
  const stranger = client("bwt_bad");
  const refusals = await Promise.all(Array.from({ length: 10 }, () => stranger.exchanges.search().catch((e) => e)));
  assert.ok(refusals.every((e) => e instanceof BitweenClientError && e.status === 400));
  assert.strictEqual(seen.signIns, 1);

  // A Bitween can make sessions a minute long: renewed halfway, it is still used.
  const { seen: brief, client: briefly } = await fakeBitween(() => [200, page()], { expiresIn: 60 });
  const bitween = briefly();
  await bitween.exchanges.search();
  await bitween.exchanges.search();
  assert.strictEqual(brief.signIns, 1);
});

test("says what Bitween refused and why", async () => {
  const { client } = await fakeBitween(() => [400, { ALREADY_RETRIED: ["This exchange was retried a moment ago."] }]);
  const error = await client().exchanges.retry("x").catch((e) => e);
  assert.ok(error instanceof BitweenClientError);
  assert.deepStrictEqual([error.status, error.code, error.message], [400, "ALREADY_RETRIED", "This exchange was retried a moment ago."]);

  const refused = await client("bwt_bad").exchanges.search().catch((e) => e);
  assert.strictEqual(refused.status, 400);
  assert.match(refused.message, /^This token isn't valid/);

  const { client: forbidding } = await fakeBitween(() => [403, null]);
  const forbidden = await forbidding().scheduledRetries.runNow("x").catch((e) => e);
  assert.deepStrictEqual([forbidden.status, forbidden.code], [403, null]);
  assert.match(forbidden.message, /permission/);
});

test("gets one exchange by id, not its retry, and finds the retry a retry made", async () => {
  const { seen, client } = await fakeBitween((r) =>
    r.filters[0]?.startsWith("Id:") ? [200, page("retry-of-x", "x")] : r.filters[0]?.startsWith("RetryFor:") ? [200, page("retry-of-x")] : [200, null]);
  const bitween = client();
  assert.strictEqual((await bitween.exchanges.get("x")).id, "x");
  assert.strictEqual(await bitween.exchanges.get("nope"), null);
  assert.strictEqual(await bitween.exchanges.retry("x", { reset: true }), "retry-of-x");

  const retry = seen.requests.find((r) => r.method === "POST");
  assert.deepStrictEqual([retry.path, retry.body], ["/api/xchanges/x/retry", { reason: "Manual retry", reset: true }]);
  assert.deepStrictEqual(seen.requests.at(-1).filters, ["RetryFor:1:x"]);
});

test("reads on past an old chain's retries to the exchange, and retries without viewing", async () => {
  // From before one retry an exchange: more retries than a page, all newer than it.
  const retries = Array.from({ length: 25 }, (_, i) => `retry-${i}`);
  const { seen, client } = await fakeBitween((r) =>
    r.method === "POST" ? [200, null]
    : r.filters[0]?.startsWith("RetryFor:") ? [403, null]
    : [200, r.query.page === "0" ? page(...retries) : page("x")]);
  const bitween = client();
  assert.strictEqual((await bitween.exchanges.get("x")).id, "x");
  assert.deepStrictEqual(seen.requests.map((r) => r.query.page), ["0", "1"]);

  // The retry is made; only looking it up is refused, so it answers with no id rather than throwing.
  assert.strictEqual(await bitween.exchanges.retry("x"), null);

  const { client: failing } = await fakeBitween((r) => r.method === "POST" ? [200, null] : [503, null]);
  assert.strictEqual(await failing().exchanges.retry("x"), null);
});

test("sends a selection as the bulk endpoints take it", async () => {
  const { seen, client } = await fakeBitween(() => [200, { willRetry: 0 }]);
  const bitween = client();
  await bitween.exchanges.bulkRetry({ ids: ["a", "b"] });
  await bitween.exchanges.previewBulkRetry({ matching: { status: "failed", partnerIds: [7] }, except: ["c"] }, { reset: true });
  await bitween.exchanges.checkExport({ matching: {} });
  await assert.rejects(bitween.exchanges.bulkRetry({}), TypeError);
  await assert.rejects(bitween.exchanges.bulkRetry({ ids: [], matching: { status: "failed" } }), TypeError);
  // An empty list would read as no filter, and widen the selection.
  await assert.rejects(bitween.exchanges.bulkRetry({ matching: { ids: [] } }), TypeError);
  await assert.rejects(bitween.exchanges.bulkRetry({ matching: { status: "failed", partnerIds: [] } }), TypeError);

  const [byId, matching, everything] = seen.requests.map((r) => [r.path, r.body]);
  assert.deepStrictEqual(byId, ["/api/xchanges/bulkretry", { ids: ["a", "b"], reason: "Bulk retry", reset: false }]);
  assert.deepStrictEqual(matching, ["/api/xchanges/bulkretrypreview",
    { filter: "filter=StatusFilter%3A1%3A3&filter=PartnerId%3A1%3A7", excludeIds: ["c"], reason: "Bulk retry", reset: true }]);
  // Nothing set still has to go as a filter, or it would read as no selection at all.
  assert.deepStrictEqual(everything, ["/api/xchanges/export/check", { filter: "filter=LatestOnly%3A1%3Afalse", excludeIds: null }]);
});

test("reads files, retry trees and scheduled retries, and streams an export", async () => {
  const { seen, client } = await fakeBitween((r) =>
    r.path === "/api/bitweendocs" ? [200, { data: "{\"order\":1}", key: r.query.documentKey }]
    : r.path === "/api/xchanges/export" ? [200, Buffer.from("PK zip"), "application/zip"]
    : [200, { rootId: "x", nodes: [], truncated: false, result: [], totalCount: 0 }]);
  const bitween = client();

  assert.strictEqual(await bitween.exchanges.readFile("files/x/input"), "{\"order\":1}");
  assert.strictEqual((await bitween.exchanges.retryTree("x")).rootId, "x");
  await bitween.scheduledRetries.search({ subscriptionId: 4, errorContains: "down", to: "2026-11-03T10:00:00Z" });
  await bitween.scheduledRetries.runNow("x");
  const zip = Buffer.from(await new Response(await bitween.exchanges.export({ ids: ["x"] })).arrayBuffer());
  assert.strictEqual(zip.toString(), "PK zip");

  const [file, tree, scheduled, runNow] = seen.requests;
  assert.strictEqual(file.query.documentKey, "files/x/input");
  assert.deepStrictEqual([tree.path, tree.query.id], ["/api/xchanges/retrytree", "x"]);
  assert.deepStrictEqual([scheduled.path, scheduled.filters],
    ["/api/delayedretries", ["SubscriptionId:1:4", "Exception:4:down", "On:8:2026-11-03T10:00:00.000Z"]]);
  assert.deepStrictEqual([runNow.method, runNow.path], ["POST", "/api/delayedretries/x/runnow"]);
});
