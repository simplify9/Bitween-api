import { screen, waitFor } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A retry group on its own page. Saving writes the policy at once, with the group put into the
 * policy as the server has it then - so what the page sends is the thing to check.
 */

const timeouts = {
  id: "g-1",
  name: "Timeouts",
  priority: 10,
  enabled: true,
  appliesTo: ["Error"],
  matchers: [{ type: "contains", value: "timeout", caseSensitive: false }],
  action: "Allow",
  budget: { maxAttemptsPerError: 3, maxAttemptsTotal: 10, delayStrategy: { type: "fixed", delayMs: 60000 } },
  notes: null,
  alertMode: "Inherit",
  alertChannelId: null,
};

/** The policy as the server has it, and whatever the page writes back to it. */
function server() {
  const written: { name: string; groups: { id: string; name: string; budget?: { maxAttemptsPerError: number } }[] }[] = [];
  const handlers = [
    http.get(apiPath("/retrypolicies/3"), () =>
      HttpResponse.json({ name: "Carrier API failures", groups: [timeouts], alertChannelId: 7 }),
    ),
    http.get(apiPath("/subscriptions"), () => HttpResponse.json({ result: [], totalCount: 0 })),
    http.get(apiPath("/notificationchannels"), () => HttpResponse.json({ 7: "Ops email" })),
    http.post(apiPath("/retrypolicies/3"), async ({ request }) => {
      written.push((await request.json()) as (typeof written)[number]);
      return new HttpResponse(null, { status: 204 });
    }),
  ];
  return { written, handlers };
}

describe("a retry group's page", () => {
  it("saves the policy with the group changed, and goes back where it came from", async () => {
    const { written, handlers } = server();
    const { user, router } = renderApp("/retry-policies/3/groups/g-1?return=%2Felsewhere", { handlers });

    const perMessage = await screen.findByLabelText("Retries per message");
    // The policy default is what Inherit means here, named rather than left for you to look up.
    expect(screen.getByText("Ops email")).toBeVisible();

    await user.clear(perMessage);
    await user.type(perMessage, "5");
    await user.click(screen.getByRole("button", { name: "Save group" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/elsewhere"));
    expect(written).toHaveLength(1);
    expect(written[0].name).toBe("Carrier API failures");
    expect(written[0].groups.map((g) => [g.id, g.budget?.maxAttemptsPerError])).toEqual([["g-1", 5]]);
  });

  it("won't bring back a group that was removed while its page was open", async () => {
    const { written, handlers } = server();
    let removed = false;
    const { user } = renderApp("/retry-policies/3/groups/g-1", {
      handlers: [
        http.get(apiPath("/retrypolicies/3"), () =>
          HttpResponse.json({ name: "Carrier API failures", groups: removed ? [] : [timeouts], alertChannelId: 7 }),
        ),
        ...handlers,
      ],
    });

    const perMessage = await screen.findByLabelText("Retries per message");
    removed = true;
    await user.clear(perMessage);
    await user.type(perMessage, "5");
    await user.click(screen.getByRole("button", { name: "Save group" }));

    expect(await screen.findByText(/This group was removed from the policy meanwhile/)).toBeVisible();
    expect(written).toHaveLength(0);
  });

  it("adds a new group after the ones the policy already has", async () => {
    const { written, handlers } = server();
    const { user, router } = renderApp("/retry-policies/3/groups/new?return=%2Felsewhere", { handlers });

    await user.type(await screen.findByLabelText("Group name"), "Refusals");
    await user.click(screen.getByRole("button", { name: "Add condition" }));
    await user.type(screen.getByLabelText("Text to find"), "refused");
    await user.click(screen.getByRole("button", { name: "Add group" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/elsewhere"));
    expect(written[0].groups.map((g) => g.name)).toEqual(["Timeouts", "Refusals"]);
  });

  it("says what is missing, at the field, instead of writing a group the server would refuse", async () => {
    const { written, handlers } = server();
    const { user } = renderApp("/retry-policies/3/groups/new", { handlers });

    // The name is a labelled field; the heading only shows what has been typed into it.
    const name = await screen.findByLabelText("Group name");
    expect(screen.getByRole("heading", { level: 1, name: "New retry group" })).toBeVisible();
    expect(screen.getByText("Still needs a name and at least one condition.")).toBeVisible();

    // Add group can be pressed with nothing filled in: each missing field is marked, the first takes focus.
    await user.click(screen.getByRole("button", { name: "Add group" }));
    expect(screen.getByText("Give the group a name, e.g. Timeouts.")).toBeVisible();
    expect(screen.getByText("Add a condition, e.g. Error contains timeout.")).toBeVisible();
    expect(name).toHaveFocus();
    expect(name).toHaveAttribute("aria-invalid", "true");

    // A mark goes as soon as its field is put right.
    await user.type(name, "No conditions");
    expect(screen.queryByText("Give the group a name, e.g. Timeouts.")).toBeNull();
    expect(screen.getByRole("heading", { level: 1, name: "No conditions" })).toBeVisible();
    await user.click(screen.getByRole("button", { name: "Add group" }));

    expect(screen.getByText("Add a condition, e.g. Error contains timeout.")).toBeVisible();
    expect(screen.getByText("Still needs at least one condition.")).toBeVisible();
    expect(written).toHaveLength(0);
  });
});
