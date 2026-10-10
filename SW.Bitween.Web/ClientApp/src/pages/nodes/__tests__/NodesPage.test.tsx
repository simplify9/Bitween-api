import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** Administration › Nodes: the cluster, as each node's heartbeat describes it. */
const node = (over: Record<string, unknown>) => ({
  name: "web-1:12",
  host: "web-1",
  startedOn: "2026-10-10T08:00:00Z",
  lastSeenOn: new Date().toISOString(),
  online: true,
  version: "10.0.61",
  dataSources: false,
  runtimes: ["dotnet", "python", "node"],
  leases: [],
  holdsDataSources: [],
  ...over,
});

describe("the Nodes page", () => {
  it("lists each node, says which are gone, and warns when nothing runs data sources or versions differ", async () => {
    renderApp("/nodes", {
      handlers: [
        http.get(apiPath("/cluster/nodes"), () =>
          HttpResponse.json({
            answeredBy: "web-1:12",
            goneAfterSeconds: 90,
            nodes: [
              node({}),
              node({ name: "web-2:40", host: "web-2", version: "10.0.62", runtimes: ["dotnet"] }),
              node({ name: "old-1:7", host: "old-1", online: false, lastSeenOn: "2026-10-09T08:00:00Z" }),
            ],
          }),
        ),
      ],
    });

    expect(await screen.findByText("web-2", undefined, { timeout: 5000 })).toBeVisible();
    expect(screen.getByText(/No node runs data sources/)).toBeVisible();
    expect(screen.getByText(/The nodes run different versions \(10\.0\.61, 10\.0\.62\)/)).toBeVisible();
    expect(screen.getByText("This page came from this node")).toBeVisible();

    const gone = screen.getByText("old-1").closest("tr")!;
    expect(within(gone).getByText("Gone")).toBeVisible();
    const web2 = screen.getByText("web-2").closest("tr")!;
    expect(within(web2).queryByText("Python")).not.toBeInTheDocument();
  });
});
