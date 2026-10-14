import { screen, within } from "@testing-library/react";
import { http, HttpResponse, type JsonBodyType, type RequestHandler } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { auditRow, auditTrail } from "./trail";

/**
 * The History card on every entity page that has one. Its absence without `audit.view` stays in
 * e2e/audit-trail.spec.ts, beside the API refusing the same session.
 */

const empty = { result: [], totalCount: 0 };
const none = (path: string) => http.get(apiPath(path), () => HttpResponse.json(empty));
const json = (path: string, body: JsonBodyType) => http.get(apiPath(path), () => HttpResponse.json(body));

/** GET /documents/{id}: `RawDocument` in src/api/http/documents.ts. */
const PURCHASE_ORDER = {
  id: 5,
  code: "PURCHASE_ORDER",
  name: "Purchase order",
  documentFormat: "Json",
  busEnabled: false,
  busMessageTypeName: null,
  duplicateInterval: 0,
  disregardsUnfilteredMessages: false,
  promotedProperties: [],
  usedByCount: 0,
  retiredOn: null,
};

/** What an information type's detail asks for besides the type itself. */
const informationTypeDetail = [
  json("/documents/5", PURCHASE_ORDER),
  none("/subscriptions"),
  none("/busgateways"),
  none("/xchanges"),
  none("/partners"),
];

/** The History panel on an entity page. */
const historyPanel = async () =>
  (await screen.findByRole("heading", { name: "History", level: 2 })).closest("section")!;

/**
 * Each area opened straight at its page, with just enough of an entity behind it to render. The
 * e2e this replaced took the first row of each area from whatever database it ran against and
 * skipped the areas that happened to be empty; here every one of them is always reached.
 */
const areas: {
  label: string;
  path: string;
  entityName: string;
  entityKey: string;
  handlers: RequestHandler[];
}[] = [
  {
    label: "partner",
    path: "/partners/11",
    entityName: "Partner",
    entityKey: "11",
    handlers: [
      json("/partners/11", { name: "Northwind", apiCredentials: [], adapterProperties: {}, secretProperties: [] }),
      none("/partners"),
      none("/apigateways"),
      none("/busgateways"),
      none("/xchanges"),
      none("/subscriptions"),
    ],
  },
  {
    label: "information type",
    path: "/information-types/5",
    entityName: "Document",
    entityKey: "5",
    handlers: informationTypeDetail,
  },
  {
    label: "work group",
    path: "/work-groups/12",
    entityName: "WorkGroup",
    entityKey: "12",
    handlers: [
      json("/workgroups", {
        result: [{ id: 12, name: "Nightly", busMessageName: "nightly", options: null, processorNodeCount: 0, usedByCount: 0 }],
        totalCount: 1,
      }),
      none("/subscriptions"),
      // The group's live queue numbers: a broker with nothing on it.
      json("/ops/summary", {
        totalConsumers: 0,
        unhealthyConsumers: 0,
        disconnectedConsumers: 0,
        totalQueueDepth: 0,
        totalRetryBacklog: 0,
        totalDeadLetterBacklog: 0,
        totalIncomingRate: 0,
        totalAckRate: 0,
        lastUpdatedUtc: "2026-09-20T10:00:00Z",
      }),
      ...["consumers", "retries", "deadletters", "alerts", "unattendedqueues"].map((p) => json(`/ops/${p}`, [])),
    ],
  },
  {
    label: "global value set",
    path: "/global-values/shared",
    entityName: "GlobalAdapterValuesSet",
    entityKey: "shared",
    handlers: [
      json("/globaladaptervaluessets/shared", { id: "shared", name: "Shared", values: {}, secretProperties: [] }),
      none("/subscriptions"),
    ],
  },
  {
    label: "retry policy",
    path: "/retry-policies/13",
    entityName: "RetryPolicy",
    entityKey: "13",
    handlers: [
      json("/retrypolicies/13", { name: "Default", groups: [], alertChannelId: null }),
      json("/notificationchannels", {}),
      none("/subscriptions"),
      http.post(apiPath("/retrypolicies/13/usage"), () => HttpResponse.json([])),
    ],
  },
  {
    label: "notifier",
    path: "/notifiers/14",
    entityName: "Notifier",
    entityKey: "14",
    handlers: [
      json("/notifiers/14", {
        id: 14,
        name: "Ops",
        inactive: false,
        handlerId: null,
        handlerProperties: [],
        runOnSuccessfulResult: false,
        runOnBadResult: false,
        runOnFailedResult: true,
        runOnSubscriptions: [],
      }),
      none("/notifications"),
      json("/adapters/Catalog", []),
      none("/subscriptions"),
    ],
  },
  {
    label: "API gateway",
    path: "/api-gateways/15",
    entityName: "ApiGateway",
    entityKey: "15",
    handlers: [
      json("/apigateways/15", { id: 15, name: "Public", urlName: "public", partnersCount: 0, inactive: false, partners: [] }),
      json("/apigateways/15/rejections", { node: "web-1:12", since: "2026-10-10T08:00:00Z", counts: {}, recent: [] }),
      none("/apigateways/attachments"),
      // What the attachments table resolves each wired subscription's columns from.
      ...["/subscriptions", "/documents", "/partners", "/workgroups", "/retrypolicies"].map((p) => none(p)),
    ],
  },
  {
    label: "subscription",
    path: "/subscriptions/16",
    entityName: "Subscription",
    entityKey: "16",
    handlers: [
      json("/subscriptions/16", {
        id: 16,
        name: "Push orders",
        documentId: 5,
        partnerId: null,
        aggregationForId: null,
        type: "ApiCall",
        handlerId: null,
        mapperId: null,
        receiverId: null,
        dataSourceId: null,
        validatorId: null,
        inactive: false,
        temporary: false,
        categoryId: null,
        handlerProperties: [],
        mapperProperties: [],
        receiverProperties: [],
        validatorProperties: [],
        documentFilter: [],
        matchExpression: null,
        workGroupId: null,
        retryPolicyId: null,
        customRetryPolicy: null,
        schedules: [],
        responseSubscriptionId: null,
        responseMessageTypeName: null,
        receiveOn: null,
        aggregateOn: null,
        pausedOn: null,
        isRunning: false,
        consecutiveFailures: 0,
        lastException: null,
      }),
      none("/apigateways"),
      json("/adapters/Catalog", []),
      json("/datasources/Providers", []),
      none("/workgroups"),
      none("/retrypolicies"),
      http.post(apiPath("/subscriptions/16/retryusage"), () => HttpResponse.json([])),
      ...informationTypeDetail,
    ],
  },
];

describe("the history card", () => {
  it.each(areas)("is on a $label's page, showing that $label's own history", async (area) => {
    const trail = auditTrail([auditRow({ entityName: area.entityName, entityKey: area.entityKey })]);
    renderApp(area.path, { handlers: [...area.handlers, trail.handler] });

    const panel = await historyPanel();
    expect(await within(panel).findByRole("row", { name: /Created/ })).toBeVisible();
    // The card is one line per page, so the thing each page can get wrong is which row it asks about.
    expect(trail.asked.at(-1)?.get("entityName")).toBe(area.entityName);
    expect(trail.asked.at(-1)?.get("entityKey")).toBe(area.entityKey);
  });

  it("is on the settings page, covering the whole area", async () => {
    const trail = auditTrail([
      auditRow({
        entityName: "Setting",
        entityKey: "Bitween.JwtExpiryMinutes",
        userId: null,
        userDisplayName: null,
        changes: { Value: { old: null, new: "60" } },
      }),
    ]);
    renderApp("/settings", {
      handlers: [
        json("/settings", [
          {
            key: "Bitween.JwtExpiryMinutes",
            section: "API behavior",
            label: "Sign-in session length (minutes)",
            description: "",
            kind: "number",
            defaultValue: "60",
            value: "60",
            secret: false,
            overridden: false,
            hasValue: true,
            editable: true,
            access: "editable",
          },
        ]),
        trail.handler,
      ],
    });

    // Settings has no per-row page, so its card covers the whole area, and each line says which
    // setting it changed, by the label the form above gives it.
    const panel = await historyPanel();
    expect(await within(panel).findByRole("row", { name: /Created/ })).toBeVisible();
    expect(within(panel).getByRole("row", { name: /Sign-in session length \(minutes\)/ })).toBeVisible();
    expect(trail.asked.at(-1)?.get("entityName")).toBe("Setting");
    expect(trail.asked.at(-1)?.has("entityKey")).toBe(false);
  });

  it("folds the scheduler's repeated writes into one line, and opens it on request", async () => {
    const nextRun = (i: number) =>
      auditRow({
        entityName: "Subscription",
        entityKey: "16",
        state: "Modified",
        userId: "scheduled-job",
        userDisplayName: null,
        occurredOn: `2026-09-20T1${i}:00:00Z`,
        changes: { ReceiveOn: { old: `2026-09-20T1${i}:00:00Z`, new: `2026-09-20T1${i + 1}:00:00Z` } },
      });
    const trail = auditTrail([
      nextRun(4),
      nextRun(3),
      nextRun(2),
      nextRun(1),
      auditRow({
        entityName: "Subscription",
        entityKey: "16",
        changes: {
          Id: { old: null, new: 16 },
          IsRunning: { old: null, new: false },
          AggregateOn: { old: null, new: null },
          Name: { old: null, new: "Push orders" },
          HandlerId: { old: null, new: "http" },
          DocumentId: { old: null, new: 5 },
          Temporary: { old: null, new: true },
          Description: { old: null, new: "Orders to the ERP" },
        },
      }),
    ]);
    const subscription = areas.find((a) => a.label === "subscription")!;
    const { user } = renderApp(subscription.path, { handlers: [...subscription.handlers, trail.handler] });

    const panel = within(await historyPanel());
    const run = await panel.findByRole("row", { name: /Scheduler, 4 times/ });
    expect(run).toHaveTextContent("Next run");
    expect(run).toHaveTextContent(/last/);
    expect(panel.getAllByRole("row", { name: /Next run/ })).toHaveLength(1);

    // The creation names what was chosen, most telling first, and leaves the system's own fields out.
    const created = panel.getByRole("row", { name: /Created/ });
    expect(created).toHaveTextContent("Name, Information type, Delivery adapter +2 more");
    expect(created).not.toHaveTextContent("Running mark");

    await user.click(within(run).getByRole("button", { name: "Show each" }));
    expect(panel.getAllByRole("row", { name: /Next run/ })).toHaveLength(5);
    await user.click(within(run).getByRole("button", { name: "Hide" }));
    expect(panel.getAllByRole("row", { name: /Next run/ })).toHaveLength(1);
  });
});
