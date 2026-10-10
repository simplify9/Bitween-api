import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** Settings › About this instance: what this Bitween is and how it is set up, read-only. */
const ABOUT = {
  version: "10.0.61",
  build: "10.0.61+abc123",
  node: { name: "bitween-7d9f", startedOn: "2026-10-10T08:00:00Z", runtime: ".NET 10.0.0", os: "Linux", dataSources: false },
  database: "PgSql",
  storage: { provider: "S3", adapterPath: "adapters", documentPrefix: "temp30/docs" },
  broker: { queuePrefix: "bitween", managementConfigured: true },
  health: [
    { name: "database", status: "Healthy", durationMs: 4, description: null, error: null },
    { name: "rabbitmq", status: "Unhealthy", durationMs: 2003, description: null, error: "Connection refused" },
  ],
  adapters: {
    runtimes: [
      { name: "dotnet", available: true, version: "Microsoft.NETCore.App 10.0.0", reason: null },
      { name: "python", available: true, version: "Python 3.12.3", reason: null },
      { name: "node", available: false, version: null, reason: "'node' is not installed on this host" },
    ],
    pip: { available: false, version: null, reason: "'python3' isn't installed" },
    npm: { available: true, version: "10.9.0", reason: null },
    commandTimeoutSeconds: 300,
    editor: { dependencies: true, memoryMb: 256, cpuCores: 1 },
  },
  limits: { signInPerMinute: 10, requestsPerMinute: 600, fileLinksPerMinute: 60000, maxResponseWaitSeconds: 60, maxRetryChainDepth: 50, staleRunAfterMinutes: 60, notifierQuietMinutes: 15 },
  network: { publicUrl: null, blockPrivateNetworkAddresses: true, trustedProxies: 0, exposeApiDocs: false, corsOrigins: [] },
  retention: { receiveAttemptRetentionDays: 30, receiveAttemptCleanupCron: "0 0 3 * * ?", inboundMessagePruneCron: "0 30 3 * * ?" },
  telemetry: { openTelemetry: false },
};

describe("About this instance", () => {
  it("shows the version, the node, its health and what adapters it can run", async () => {
    renderApp("/settings?section=About+this+instance", {
      handlers: [
        http.get(apiPath("/settings"), () => HttpResponse.json([])),
        http.get(apiPath("/settings/about"), () => HttpResponse.json(ABOUT)),
        http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
      ],
    });

    expect(await screen.findByText("10.0.61", undefined, { timeout: 5000 })).toBeVisible();
    expect(screen.getByText("bitween-7d9f")).toBeVisible();
    expect(screen.getByText("Bitween:BusProvidersEnabled is off here")).toBeVisible();
    expect(screen.getByText("Connection refused")).toBeVisible();

    const nodeRow = screen.getByText("Node.js").closest("div")!;
    expect(within(nodeRow).getByText("Missing")).toBeVisible();
    // Dependencies are on, but pip isn't here: said where it matters.
    expect(screen.getByText(/pip isn't on this node/)).toBeVisible();
  });
});
