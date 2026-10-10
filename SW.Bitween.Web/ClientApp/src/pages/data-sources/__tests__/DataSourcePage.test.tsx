import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * Saving a data source. Placement used to be left out of every save, and the server read a
 * missing placement as Auto, so renaming an Exclusive or PerNode source moved where it ran.
 */
const SOURCE = {
  id: 5,
  name: "ERP database",
  adapterId: "bitween.db.postgresql",
  kind: "Relational",
  placement: "Exclusive",
  inactive: false,
  deduplicationWindowDays: 0,
  softMemoryLimitMb: 0,
  hardMemoryLimitMb: 0,
  cpuPercentLimit: 0,
  cpuLimitSamples: 0,
  gatewayCount: 0,
  subscriptionCount: 0,
  lastKnownState: null,
  lastHeartbeatOn: null,
  lastException: null,
  consecutiveFailures: 0,
  ownedByNode: null,
  properties: { Host: "db.local" },
  secretProperties: [],
};

function backend() {
  const saves: Record<string, unknown>[] = [];
  const handlers = [
    http.get(apiPath("/datasources/Providers"), () => HttpResponse.json([])),
    http.get(apiPath("/datasources/5/telemetry"), () => HttpResponse.json({ runningHere: false, details: {} })),
    http.get(apiPath("/datasources/5"), () => HttpResponse.json(SOURCE)),
    http.post(apiPath("/datasources/5"), async ({ request }) => {
      saves.push((await request.json()) as Record<string, unknown>);
      return new HttpResponse(null, { status: 204 });
    }),
    // The catalog panel asks the adapter what the database holds; here, nothing is running.
    http.post(apiPath("/datasources/5/inspect"), () => HttpResponse.json({ succeeded: false, error: "Not running here", items: [] })),
    http.get(apiPath("/datasourcestatements"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.get(apiPath("/busgateways"), () => HttpResponse.json({ result: [], totalCount: 0 })),
  ];
  return { saves, handlers };
}

describe("saving a data source", () => {
  it("keeps its placement through a save that changes something else", async () => {
    const { saves, handlers } = backend();
    const { user } = renderApp("/data-sources/5", { handlers });

    const name = await screen.findByLabelText("Name", undefined, { timeout: 5000 });
    expect(screen.getByLabelText("Runs on")).toHaveValue("Exclusive");
    await user.type(name, " (primary)");
    await user.click(screen.getByRole("button", { name: /Save/ }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].placement).toBe("Exclusive");
    expect(saves[0].name).toBe("ERP database (primary)");
  });

  it("saves the placement chosen", async () => {
    const { saves, handlers } = backend();
    const { user } = renderApp("/data-sources/5", { handlers });

    await user.selectOptions(await screen.findByLabelText("Runs on", undefined, { timeout: 5000 }), "PerNode");
    await user.click(screen.getByRole("button", { name: /Save/ }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].placement).toBe("PerNode");
  });
});
