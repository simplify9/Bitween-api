import { expect, request, test as setup, type APIRequestContext } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD, API, BASE_URL } from "./env";
import { SEED } from "./seed-data";

/**
 * Puts the database into the state the suite expects, before any spec runs.
 *
 * Two jobs, in this order. First it removes what earlier runs left behind: a test that fails
 * part-way skips its own cleanup, and the leftovers aren't harmless — a stray account still
 * holding Administrator is enough to make the last-administrator guard pass, which strips the
 * seeded admin's role and fails everything after it. Then it makes sure the rows the specs build
 * on exist (see seed-data.ts), finding each by name and creating only what is missing, so it is
 * as happy on a database created a minute ago as on a team database that has had them for months.
 *
 * It is a setup project rather than a globalSetup so that a failure here is reported like any
 * other test — with a trace and a step that says which prerequisite is missing — rather than as
 * a stack trace before the run starts. Everything goes through the same HTTP API the UI uses.
 */

const ADMINISTRATOR_ROLE_ID = 1;

const TEST_EMAIL = /^pw-.*@example\.test$/;
const TEST_ROLE = /^PW /;
/** Everything the suite creates is named this way, so it can be found again and removed. */
const TEST_NAME = /^Playwright /;
/** The only settings the suite writes to — see the reset below for why this is a list, not "all". */
const TEST_SETTINGS = ["Theme.PrimaryColor", "Theme.TabTitle", "Theme.CompanyName"];

interface Account {
  id: number;
  email: string;
  roles: { id: number; name: string }[] | null;
}
interface Named {
  id: number;
  name: string;
}
interface RawDocument {
  code: string | null;
  name: string;
  documentFormat: string;
  busEnabled: boolean;
  busMessageTypeName: string | null;
  duplicateInterval: number;
  disregardsUnfilteredMessages: boolean;
  promotedProperties: { key: string; value: string }[] | null;
}

// One serial chain: if the backend can't be reached there is no point purging, and if the purge
// fails the seed would be built on top of whatever it left.
setup.describe.configure({ mode: "serial" });

let api: APIRequestContext;
let token = "";
const auth = () => ({ Authorization: `Bearer ${token}` });

async function signIn() {
  const login = await api.post(`${API}/accounts/login`, {
    data: { Username: ADMIN_EMAIL, Password: ADMIN_PASSWORD },
  });
  if (!login.ok())
    throw new Error(
      `Can't sign in as ${ADMIN_EMAIL} (${login.status()}: ${await login.text()}). ` +
        "Set E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD to the administrator of the instance under test. " +
        "A fresh install takes its password from Bitween__InitialAdminPassword, which tools/e2e.sh sets " +
        "to E2E_ADMIN_PASSWORD; an older dev database still has the published default.",
    );
  token = (await login.json()).jwt;
}

/** A JSON body, or a failure that says which call it was and what the server answered. */
async function ok<T>(what: string, res: Awaited<ReturnType<APIRequestContext["get"]>>): Promise<T> {
  if (!res.ok()) throw new Error(`${what} failed: ${res.status()} ${await res.text()}`);
  const text = await res.text();
  return (text ? JSON.parse(text) : null) as T;
}

/**
 * The row with exactly this name, or undefined. Searched by name rather than listed whole, so a
 * long-lived database with thousands of rows can't push the one wanted off the page.
 */
async function findByName(list: string, name: string): Promise<Named | undefined> {
  const filter = encodeURIComponent(`Name:4:${name}`);
  const res = await ok<{ result: Named[] }>(
    `searching ${list} for "${name}"`,
    await api.get(`${API}/${list}?filter=${filter}&page=0&size=100`, { headers: auth() }),
  );
  return (res.result ?? []).find((r) => r.name === name);
}

/**
 * The information type with this name, created if missing. One that exists already — on a team
 * database, say — keeps everything it has; only what the specs rely on and it lacks is added:
 * a code where it has none, and any of the wanted promoted properties. The update replaces the
 * whole row, so it is sent back with every field as read.
 */
async function ensureDocument(
  name: string,
  want: {
    code: string;
    busEnabled: boolean;
    busMessageTypeName?: string;
    promotedProperties: { key: string; value: string }[];
  },
): Promise<number> {
  const found = await findByName("documents", name);
  if (!found)
    return ok<number>(
      `creating information type "${name}"`,
      await api.post(`${API}/documents`, {
        headers: auth(),
        data: {
          name,
          documentFormat: "Json",
          duplicateInterval: 0,
          disregardsUnfilteredMessages: false,
          validationSchema: "",
          ...want,
        },
      }),
    );

  const doc = await ok<RawDocument>(
    `reading information type "${name}"`,
    await api.get(`${API}/documents/${found.id}`, { headers: auth() }),
  );
  expect(doc.documentFormat, `information type "${name}" must be JSON`).toBe("Json");
  if (want.busEnabled) expect(doc.busEnabled, `information type "${name}" must be bus-enabled`).toBe(true);

  const have = doc.promotedProperties ?? [];
  const missing = want.promotedProperties.filter((w) => !have.some((h) => h.value === w.value));
  if (doc.code && missing.length === 0) return found.id;

  await ok(
    `completing information type "${name}"`,
    await api.post(`${API}/documents/${found.id}`, {
      headers: auth(),
      data: {
        id: found.id,
        code: doc.code || want.code,
        name: doc.name,
        documentFormat: doc.documentFormat,
        busEnabled: doc.busEnabled,
        busMessageTypeName: doc.busMessageTypeName ?? undefined,
        duplicateInterval: doc.duplicateInterval,
        disregardsUnfilteredMessages: doc.disregardsUnfilteredMessages,
        promotedProperties: [...have, ...missing],
      },
    }),
  );
  return found.id;
}

setup.beforeAll(async () => {
  api = await request.newContext({ ignoreHTTPSErrors: true });
});

setup.afterAll(async () => {
  await api?.dispose();
});

setup("the backend is up and the administrator can sign in", async () => {
  const health = await api.get(`${BASE_URL}health`).catch((e: Error) => e);
  if (health instanceof Error || !health.ok())
    throw new Error(
      `Nothing is answering at ${BASE_URL}health. Start Bitween there first — tools/e2e.sh starts a ` +
        "throwaway instance with everything it needs — or point E2E_BASE_URL at one that is running.",
    );

  await signIn();

  // The seeded admin used to ship with a published password, so a database migrated back then
  // flags it and issues it a token that grants nothing until someone chooses a new one. Setting
  // it back to the same value clears the flag, which is the whole of what the suite needs: every
  // spec signs in as this account, and none of them is about the forced change itself. On a fresh
  // install the password came from Bitween__InitialAdminPassword and was never flagged, and this
  // changes nothing.
  //
  // Deliberately loud about what it is. Re-setting a known password is exactly the thing the flag
  // exists to prevent — it is defensible here only because this database exists to be tested on.
  await api.post(`${API}/accounts/changePassword`, {
    headers: auth(),
    data: { OldPassword: ADMIN_PASSWORD, NewPassword: ADMIN_PASSWORD },
  });
});

setup("leftovers from earlier runs are removed", async () => {
  const accounts = await api.get(`${API}/accounts?limit=500`, { headers: auth() });

  if (!accounts.ok()) {
    // There used to be a fallback here: a second sign-in against credentials held in
    // configuration, which worked even for an admin holding no roles. That endpoint shipped with a
    // published default password and has been removed, so this is now the only way in — and if the
    // seeded admin has lost Administrator, no request can put it back.
    throw new Error(
      `Signed in as ${ADMIN_EMAIL} but can't list accounts (${accounts.status()}). The admin has ` +
        "probably lost its Administrator role, and the database needs restoring.",
    );
  }

  const rows: Account[] = (await accounts.json()).result ?? [];

  // The seeded admin must hold Administrator, or nothing downstream can manage anything.
  const admin = rows.find((a) => a.email.toLowerCase() === ADMIN_EMAIL.toLowerCase());
  if (admin && !(admin.roles ?? []).some((r) => r.id === ADMINISTRATOR_ROLE_ID)) {
    await api.post(`${API}/accounts/${admin.id}/setRoles`, {
      headers: auth(),
      data: { roleIds: [ADMINISTRATOR_ROLE_ID] },
    });
    // Re-read as the repaired admin so the purge below runs with full permissions.
    await signIn();
  }

  for (const account of rows.filter((a) => TEST_EMAIL.test(a.email)))
    await api.post(`${API}/accounts/${account.id}/remove`, { headers: auth(), data: {} });

  const roles = await api.get(`${API}/roles?pageSize=500`, { headers: auth() });
  for (const role of ((await roles.json()).result ?? []) as Named[])
    if (TEST_ROLE.test(role.name)) await api.delete(`${API}/roles/${role.id}`, { headers: auth() });

  // Settings tests assert against product defaults, so a value left behind by a failed run would
  // make them fail for the wrong reason. Only the keys the tests actually touch are reset: the
  // Settings table is now the only home for values like the MSAL ids and the Rebex license key —
  // configuration is read once at first boot and ignored after that — so a blanket reset here
  // would destroy real configuration with no way to get it back.
  for (const key of TEST_SETTINGS)
    await api.delete(`${API}/settings/${encodeURIComponent(key)}`, { headers: auth() });

  // The domain objects the suite creates outlive a failed run too, and they are not harmless
  // either: enough of them push a newly created row off the first page of a list, and the tests
  // that look for their own row then fail for a reason that has nothing to do with them.
  //
  // Order matters. A gateway holds attachments and routes that reference subscriptions, so the
  // gateways go first or the subscriptions underneath them refuse to delete.
  const purge = async (list: string, remove: (id: number) => Promise<unknown>) => {
    const res = await api.get(`${API}/${list}?size=500&limit=500`, { headers: auth() });
    if (!res.ok()) return;
    const rows = ((await res.json()).result ?? []) as Named[];
    for (const row of rows.filter((r) => TEST_NAME.test(r.name ?? ""))) {
      // Best effort: something still referencing a row is not a reason to abandon the rest.
      try {
        await remove(row.id);
      } catch {
        /* leave it for the next run */
      }
    }
  };

  await purge("apigateways", (id) => api.delete(`${API}/apigateways/${id}`, { headers: auth() }));
  await purge("busgateways", (id) => api.delete(`${API}/busgateways/${id}`, { headers: auth() }));
  await purge("subscriptions", (id) => api.delete(`${API}/subscriptions/${id}`, { headers: auth() }));
  await purge("workgroups", (id) =>
    api.post(`${API}/workgroups/${id}/delete`, { headers: auth(), data: {} }),
  );
  await purge("documents", (id) => api.delete(`${API}/documents/${id}`, { headers: auth() }));
  await purge("partners", (id) => api.delete(`${API}/partners/${id}`, { headers: auth() }));

  // Values sets are keyed by a string id rather than a number, so `purge` cannot do them.
  const sets = await api.get(`${API}/globaladaptervaluessets?size=500&limit=500`, { headers: auth() });
  if (sets.ok())
    for (const set of ((await sets.json()).result ?? []) as { id: string; name: string }[])
      if (TEST_NAME.test(set.name ?? ""))
        await api.post(`${API}/globaladaptervaluessets/${set.id}/delete`, {
          headers: auth(),
          data: {},
        });
});

setup("the pieces the specs need are installed", async () => {
  setup.setTimeout(90_000);

  // Exchanges are processed by the app's own RabbitMQ consumers. Without them every exchange the
  // seed creates stays "processing" forever, and the wait below would time out saying only that.
  //
  // Polled rather than read once: an app that has only just started answers before its consumers
  // are attached, and the broker's management API, which this reads through, lags behind it.
  const attached = async () => {
    const consumers = await ok<{ name: string; totalNodes: number }[]>(
      "reading the RabbitMQ consumers (/api/ops/consumers)",
      await api.get(`${API}/ops/consumers`, { headers: auth() }),
    );
    return consumers.filter((c) => c.totalNodes > 0).length;
  };
  await expect
    .poll(attached, {
      message:
        "No RabbitMQ consumer is attached to Bitween's queues, so no exchange would ever be processed. " +
        "Is the broker in ConnectionStrings__RabbitMQ reachable, and Bitween__RabbitMqManagementUrl set?",
      timeout: 60_000,
    })
    .toBeGreaterThan(0);

  // The specs build every subscription out of the two in-process HTTP adapters.
  for (const [kind, key] of [
    ["handlers", "NativeHttpHandler"],
    ["receivers", "NativeHttpReceiver"],
  ]) {
    const catalog = await ok<{ key: string }[]>(
      `reading the ${kind} catalog`,
      await api.get(`${API}/adapters/Catalog?prefix=${kind}`, { headers: auth() }),
    );
    expect(
      catalog.map((a) => a.key),
      `${key} is missing from /api/adapters/Catalog?prefix=${kind}`,
    ).toContain(key);
  }
});

setup("the seeded rows exist", async () => {
  setup.setTimeout(120_000);

  const partnerId =
    (await findByName("partners", SEED.partner))?.id ??
    (await ok<number>(
      `creating partner "${SEED.partner}"`,
      await api.post(`${API}/partners`, {
        headers: auth(),
        data: { name: SEED.partner, adapterProperties: {}, secretProperties: [], loginIdentity: null },
      }),
    ));

  const filterProperty = { key: SEED.busFilterProperty.key, value: SEED.busFilterProperty.path };
  const shipmentOrderId = await ensureDocument(SEED.informationType, {
    code: SEED.informationTypeCode,
    busEnabled: false,
    promotedProperties: [],
  });
  const deliveryProofId = await ensureDocument(SEED.busInformationType, {
    code: SEED.busInformationTypeCode,
    busEnabled: true,
    busMessageTypeName: SEED.busMessageTypeName,
    promotedProperties: [filterProperty],
  });

  // The bus gateway spec creates the gateway for this type, and a type can only have one. The
  // suite's own leftovers were purged above, so anything still here belongs to somebody else.
  const gateways = await ok<{ result: (Named & { documentId: number })[] }>(
    "listing bus gateways",
    await api.get(`${API}/busgateways?page=0&size=500`, { headers: auth() }),
  );
  const claimed = (gateways.result ?? []).find((g) => g.documentId === deliveryProofId);
  expect(
    claimed,
    `bus gateway "${claimed?.name}" already listens for "${SEED.busInformationType}", which the ` +
      "bus gateway spec needs free. Delete that gateway, or move it to another information type.",
  ).toBeUndefined();

  // The failing subscription. Created live (inactive: false) because exchanges addressed to an
  // inactive subscription are never delivered, and without a retry policy so a failure stays a
  // failure instead of turning into a scheduled retry.
  const existing = await findByName("subscriptions", SEED.failingSubscription);
  const subscriptionId =
    existing?.id ??
    (await ok<number>(
      `creating subscription "${SEED.failingSubscription}"`,
      await api.post(`${API}/subscriptions`, {
        headers: auth(),
        data: {
          name: SEED.failingSubscription,
          documentId: shipmentOrderId,
          type: "ApiCall",
          partnerId,
          handlerId: "NativeHttpHandler",
          handlerProperties: [{ key: "Url", value: SEED.failingUrl }],
          receiverProperties: [],
          validatorProperties: [],
          mapperProperties: [],
          documentFilter: [],
          retryPolicyId: null,
          customRetryPolicy: null,
          runOnBadResponses: false,
          inactive: false,
        },
      }),
    ));

  // On a long-lived database someone may have switched it off or paused it; either stops the
  // exchanges below from ever being delivered.
  if (existing) {
    const raw = await ok<{ inactive: boolean; pausedOn: string | null }>(
      "reading the failing subscription",
      await api.get(`${API}/subscriptions/${subscriptionId}`, { headers: auth() }),
    );
    expect(
      raw.inactive || raw.pausedOn !== null,
      `subscription "${SEED.failingSubscription}" is disabled or paused; enable and resume it`,
    ).toBe(false);
  }

  // Every exchange it gets fails, so failures that nobody has retried are a matter of sending
  // enough of them. The specs retry some on every run, and each retry fails in turn and leaves a
  // new un-retried attempt at the end of its chain — so after the first run this usually sends
  // nothing at all.
  const unretried = async () => {
    const filter = ["StatusFilter:1:3", "LatestOnly:1:true", `SubscriptionId:1:${subscriptionId}`]
      .map((f) => `filter=${encodeURIComponent(f)}`)
      .join("&");
    const res = await ok<{ totalCount: number }>(
      "counting failed exchanges",
      await api.get(`${API}/xchanges?${filter}&page=0&size=1`, { headers: auth() }),
    );
    return res.totalCount;
  };

  const missing = SEED.unretriedFailures - (await unretried());
  for (let i = 0; i < missing; i++)
    await ok(
      "creating an exchange",
      await api.post(`${API}/xchanges`, {
        headers: auth(),
        data: {
          option: "SubscriberId",
          subscriberId: subscriptionId,
          data: JSON.stringify({ shipment: `e2e-seed-${Date.now()}-${i}` }),
        },
      }),
    );

  await expect
    .poll(unretried, {
      message:
        `the exchanges sent to "${SEED.failingSubscription}" were never processed — are Bitween's ` +
        "RabbitMQ consumers running?",
      timeout: 90_000,
    })
    .toBeGreaterThanOrEqual(SEED.unretriedFailures);
});
