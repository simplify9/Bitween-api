import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { ALL_PERMISSIONS, apiPath, renderApp } from "../../../__tests__/support/renderApp";

const empty = { result: [], totalCount: 0 };

const openPartner = (writes: unknown[], permissions = ALL_PERMISSIONS) => {
  let needed = false;
  return renderApp("/partners/7", {
    as: { permissions },
    handlers: [
      http.get(apiPath("/partners/7"), () =>
        HttpResponse.json({
          name: "Acme",
          apiCredentials: [],
          adapterProperties: { webhookUrl: "https://acme/in", internalCode: "X9" },
          secretProperties: [],
          changesNeedApproval: needed,
          portalAccess: { webhookUrl: "visible" },
        }),
      ),
      http.post(apiPath("/partners/7"), async ({ request }) => {
        writes.push(await request.json());
        return HttpResponse.json(null);
      }),
      http.post(apiPath("/partners/7/approval"), async ({ request }) => {
        const body = (await request.json()) as { changesNeedApproval: boolean };
        writes.push(body);
        needed = body.changesNeedApproval;
        return new HttpResponse(null, { status: 204 });
      }),
      ...["/partners", "/apigateways", "/busgateways", "/xchanges", "/subscriptions", "/audit"].map((p) =>
        http.get(apiPath(p), () => HttpResponse.json(empty)),
      ),
    ],
  });
};

describe("whether a partner's portal changes need approval", () => {
  it("is saved the moment it's ticked, apart from the partner's details", async () => {
    const writes: unknown[] = [];
    const { user } = openPartner(writes);

    const box = await screen.findByRole("checkbox", { name: /Their changes need our approval/ });
    expect(box).not.toBeChecked();
    await user.click(box);

    await waitFor(() => expect(writes).toEqual([{ changesNeedApproval: true }]));
    // Enabled again only once the save and the refetch after it are done, so this is the saved state.
    await waitFor(() => expect(box).toBeEnabled());
    expect(box).toBeChecked();
    expect(screen.queryByRole("button", { name: "Save changes" })).not.toBeInTheDocument();
  });

  it("can't be changed without the right to edit partners", async () => {
    openPartner([], ALL_PERMISSIONS.filter((p) => p !== "partners.edit"));
    expect(await screen.findByRole("checkbox", { name: /Their changes need our approval/ })).toBeDisabled();
  });

  it("sets what they see of each property, saved with the partner", async () => {
    const writes: unknown[] = [];
    const { user } = openPartner(writes);

    const choices = await screen.findByRole("radiogroup", { name: "What they see of internalCode" });
    expect(within(choices).getByRole("radio", { name: "Hidden" })).toBeChecked();
    await user.click(within(choices).getByRole("radio", { name: "Can edit" }));
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await waitFor(() => expect(writes).toHaveLength(1));
    expect((writes[0] as { portalAccess: unknown }).portalAccess).toEqual({ internalCode: "editable", webhookUrl: "visible" });
  });
});
