import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/** Where bitween login sends the browser: asks first, then hands a one-time code back to the CLI. */
const CHALLENGE = "a".repeat(43);
const STATE = "state-123";

function grant() {
  const calls: Array<Record<string, string>> = [];
  const handlers = [
    http.post(apiPath("/accounts/cligrant"), async ({ request }) => {
      calls.push((await request.json()) as Record<string, string>);
      return HttpResponse.json({ code: "the-code" });
    }),
  ];
  return { calls, handlers };
}

describe("signing the bitween CLI in", () => {
  const assign = vi.fn();
  const original = window.location;

  afterEach(() => {
    Object.defineProperty(window, "location", { configurable: true, value: original });
    assign.mockReset();
  });

  const stubLocation = () =>
    Object.defineProperty(window, "location", { configurable: true, value: { ...original, assign } });

  it("asks before granting, then sends the code to the CLI on this machine's loopback", async () => {
    const { calls, handlers } = grant();
    const { user } = renderApp(`/cli-login?challenge=${CHALLENGE}&state=${STATE}&port=53123`, { handlers });
    expect(calls).toHaveLength(0);
    stubLocation();

    await user.click(await screen.findByRole("button", { name: "Sign in the CLI" }));

    await vi.waitFor(() =>
      expect(assign).toHaveBeenCalledWith(`http://127.0.0.1:53123/callback?code=the-code&state=${STATE}`),
    );
    expect(calls).toEqual([{ codeChallenge: CHALLENGE }]);
  });

  it("tells the CLI it was cancelled, and grants nothing", async () => {
    const { calls, handlers } = grant();
    const { user } = renderApp(`/cli-login?challenge=${CHALLENGE}&state=${STATE}&port=53123`, { handlers });
    stubLocation();

    await user.click(await screen.findByRole("button", { name: "Cancel" }));

    expect(assign).toHaveBeenCalledWith(`http://127.0.0.1:53123/callback?error=cancelled&state=${STATE}`);
    expect(calls).toHaveLength(0);
  });

  it("shows the code to paste when the CLI has no listener here", async () => {
    const { handlers } = grant();
    const { user } = renderApp(`/cli-login?challenge=${CHALLENGE}&state=${STATE}`, { handlers });

    await user.click(await screen.findByRole("button", { name: "Sign in the CLI" }));

    expect(await screen.findByText("the-code")).toBeInTheDocument();
  });

  it("refuses a link that isn't from the CLI", async () => {
    const { calls, handlers } = grant();
    renderApp(`/cli-login?challenge=short&state=${STATE}&port=80`, { handlers });

    expect(await screen.findByText(/isn't a sign-in request from the bitween CLI/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sign in the CLI" })).not.toBeInTheDocument();
    expect(calls).toHaveLength(0);
  });
});
