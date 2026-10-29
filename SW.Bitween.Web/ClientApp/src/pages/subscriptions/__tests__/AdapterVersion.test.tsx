import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { backend, ORDERS_ADAPTER, sub } from "./subscriptionBackend";

/**
 * Pinning a delivery adapter to a published version: what the Delivery step offers, what a save
 * sends, and that a save touching something else keeps the pin. What the server accepts is
 * SW.Bitween.IntegrationTests' business (AdapterCatalogTests).
 */

// The picker's dropdown watches its anchor's size when it closes, which jsdom cannot measure.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
} as unknown as typeof ResizeObserver;

describe("pinning an adapter version", () => {
  it("shows what the adapter says about itself and follows the current version by default", async () => {
    const { handlers } = backend([sub()]);
    renderApp("/subscriptions/10?stage=delivery", { handlers });

    expect(await screen.findByText(/Sends orders to Acme\./)).toBeVisible();
    expect(screen.getByText(/by Acme Ltd/)).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Adapter version" })).toHaveDisplayValue("Follow current (v1.1.0)");
  });

  it("saves the version picked", async () => {
    const { handlers, saves } = backend([sub()]);
    const { user } = renderApp("/subscriptions/10?stage=delivery", { handlers });

    await user.selectOptions(await screen.findByRole("combobox", { name: "Adapter version" }), "1.0.0");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].handlerVersion).toBe("1.0.0");
    expect(saves[0].handlerId).toBe("acme.handlers.orders");
  });

  it("names each version's runtime when they differ, and what the pinned one runs on", async () => {
    const [first, second] = ORDERS_ADAPTER.versionHistory;
    const { handlers } = backend([sub()], {
      ...ORDERS_ADAPTER,
      versionHistory: [
        { ...first, runtime: "dotnet" },
        { ...second, runtime: "python" },
      ],
    });
    const { user } = renderApp("/subscriptions/10?stage=delivery", { handlers });

    const picker = await screen.findByRole("combobox", { name: "Adapter version" });
    expect(within(picker).getByRole("option", { name: "v1.0.0 · .NET" })).toBeInTheDocument();
    expect(within(picker).getByRole("option", { name: "v1.1.0 · Python" })).toBeInTheDocument();
    expect(screen.getByText(/Custom · Python · v1\.1\.0/)).toBeVisible();

    await user.selectOptions(picker, "1.0.0");
    expect(await screen.findByText(/Custom · \.NET · v1\.0\.0/)).toBeVisible();
  });

  it("keeps the pin through a save that changes something else", async () => {
    const { handlers, saves } = backend([sub({ handlerVersion: "1.0.0" })]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    await user.click(await screen.findByRole("radio", { name: "Off" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Turn off" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].handlerVersion).toBe("1.0.0");
  });
});

/**
 * A retry policy written on the subscription itself, which only the API can set. Every UI save
 * used to send it as null, so renaming or disabling a subscription erased it without a word.
 */
describe("a retry policy set on the subscription through the API", () => {
  const inline = { groups: [{ name: "5xx", maxRetries: 3 }] };

  it("is described, and kept through a save that changes something else", async () => {
    const { handlers, saves } = backend([sub({ customRetryPolicy: inline })]);
    const { user } = renderApp("/subscriptions/10", { handlers });

    expect(await screen.findByText(/Retries by 1 rule group set on this subscription through the API/)).toBeVisible();

    await user.click(await screen.findByRole("radio", { name: "Off" }));
    await user.click(within(await screen.findByRole("dialog")).getByRole("button", { name: "Turn off" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].customRetryPolicy).toEqual(inline);
    expect(saves[0].retryPolicyId).toBeNull();
  });

  it("is replaced when a named policy is picked", async () => {
    const { handlers, saves } = backend([sub({ customRetryPolicy: inline })]);
    const { user } = renderApp("/subscriptions/10", {
      handlers: [
        http.get(apiPath("/retrypolicies"), () =>
          HttpResponse.json({ result: [{ id: 4, name: "Standard", groups: [] }], totalCount: 1 }),
        ),
        ...handlers,
      ],
    });

    await user.click(await screen.findByRole("combobox", { name: /Retry policy/ }));
    await user.click(await screen.findByRole("option", { name: "Standard" }));
    expect(await screen.findByText(/Saving replaces the retry rules set on this subscription/)).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(saves).toHaveLength(1));
    expect(saves[0].retryPolicyId).toBe(4);
    expect(saves[0].customRetryPolicy).toBeNull();
  });
});

describe("a version the registry has revoked", () => {
  const updates = (revokedVersion: string) =>
    http.get(apiPath("/marketplace/updates"), () =>
      HttpResponse.json({
        configured: true,
        checkedOn: "2026-10-13T00:00:00Z",
        error: null,
        adapters: [
          {
            adapterId: "acme.handlers.orders", current: "1.1.0", latest: null, updateAvailable: false, newer: [],
            revoked: [{ version: revokedVersion, reason: "Leaks the token in logs.", revokedOn: null }],
            listed: true, checkedOn: "2026-10-13T00:00:00Z",
          },
        ],
      }),
    );

  it("is named on a subscription pinned to it", async () => {
    const { handlers } = backend([sub({ handlerVersion: "1.0.0" })]);
    renderApp("/subscriptions/10", { handlers: [updates("1.0.0"), ...handlers] });
    const notice = (await screen.findByText(/which the registry has revoked/)).closest("p")!;
    expect(notice).toHaveTextContent(
      "The handler runs acme.handlers.orders v1.0.0, which the registry has revoked: Leaks the token in logs.",
    );
  });

  it("is named on one following current when current is revoked, and not when another version is", async () => {
    const { handlers } = backend([sub()]);
    renderApp("/subscriptions/10", { handlers: [updates("1.1.0"), ...handlers] });
    expect(await screen.findByText(/v1\.1\.0, which the registry has revoked/)).toBeVisible();
  });

  it("isn't named when the subscription runs another version", async () => {
    const { handlers } = backend([sub({ handlerVersion: "1.1.0" })]);
    renderApp("/subscriptions/10", { handlers: [updates("1.0.0"), ...handlers] });
    expect(await screen.findByRole("radio", { name: "Off" })).toBeVisible();
    expect(screen.queryByText(/which the registry has revoked/)).not.toBeInTheDocument();
  });
});
