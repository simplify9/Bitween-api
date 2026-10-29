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

  it("shows each node's last day, flags one that fails more than the rest, and lists resident instances", async () => {
    const day = (over: Record<string, unknown>) => ({
      host: "",
      alive: true,
      lastSeenOn: new Date().toISOString(),
      exchanges: 0,
      failed: 0,
      successRate: null,
      p95Ms: null,
      adapterCalls: 0,
      adapterFailures: 0,
      timeouts: 0,
      limitStops: 0,
      crashes: 0,
      resident: [],
      ...over,
    });
    renderApp("/nodes", {
      handlers: [
        http.get(apiPath("/cluster/nodes"), () =>
          HttpResponse.json({
            answeredBy: "web-1:12",
            goneAfterSeconds: 90,
            nodes: [node({ dataSources: true }), node({ name: "web-2:40", host: "web-2", dataSources: true })],
          }),
        ),
        http.get(apiPath("/metrics/nodes"), () =>
          HttpResponse.json({
            nodes: [
              day({ id: "web-1:12", exchanges: 1000, failed: 5, p95Ms: 300, adapterCalls: 2000 }),
              day({
                id: "web-2:40",
                exchanges: 1000,
                failed: 200,
                adapterCalls: 2000,
                timeouts: 150,
                resident: [{ a: "acme.handlers.orders", s: "Ready", pid: 4410, mb: 182, cpu: 3.5, up: 7200, r: 2, q: false, f: 1, e: null }],
              }),
            ],
          }),
        ),
      ],
    });

    const web2 = (await screen.findByText("web-2", undefined, { timeout: 5000 })).closest("tr")!;
    expect(await within(web2).findByText("Fails more than the others")).toBeVisible();
    expect(within(web2).getByText(/150 timed out/)).toBeVisible();
    const web1 = screen.getByText("web-1").closest("tr")!;
    expect(within(web1).queryByText("Fails more than the others")).not.toBeInTheDocument();

    const instances = within(screen.getByRole("heading", { name: "Resident adapter instances" }).closest("section")!);
    const row = instances.getByText("acme.handlers.orders").closest("tr")!;
    expect(within(row).getByText("182 MB")).toBeVisible();
    expect(within(row).getByText("2h 0m")).toBeVisible();
  });
});
