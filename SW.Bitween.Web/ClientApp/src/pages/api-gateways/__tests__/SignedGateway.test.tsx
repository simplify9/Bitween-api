import { screen, within } from "@testing-library/react";
import { http, HttpResponse, type JsonBodyType } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A gateway that takes signed calls (webhooks), on its page. That the server holds to the signature
 * is SignedGatewayTests; this is the admin's side: setting it up and reading back how to call it.
 */
const empty = { result: [], totalCount: 0 };
const none = (path: string) =>
  http.get(apiPath(path), () => HttpResponse.json(empty));
const json = (path: string, body: JsonBodyType) =>
  http.get(apiPath(path), () => HttpResponse.json(body));

const KEYS = {
  method: "PartnerKey",
  keyHeader: null,
  issuer: null,
  audience: null,
  partnerClaim: null,
};
const SIGNED = {
  method: "Signature",
  keyHeader: null,
  issuer: null,
  audience: null,
  partnerClaim: null,
  signatureHeader: "X-Hook-Signature",
  signatureScheme: "TimestampedHmacSha256",
  signatureSecretProperty: "WebhookSecret",
  signatureToleranceSeconds: null,
};

const openGateway = (
  authentication: JsonBodyType,
  onSave?: (body: Record<string, unknown>) => void,
) =>
  renderApp("/api-gateways/15", {
    handlers: [
      http.post(apiPath("/apigateways/15"), async ({ request }) => {
        onSave?.((await request.json()) as Record<string, unknown>);
        return HttpResponse.json(null);
      }),
      json("/apigateways/15", {
        id: 15,
        name: "Hooks",
        urlName: "hooks",
        partnersCount: 0,
        inactive: false,
        partners: [],
        authentication,
        defaultKeyHeader: "partnerkey",
      }),
      json("/apigateways/attachments", empty),
      json("/apigateways/15/rejections", {
        node: "web-1:12",
        since: "2026-10-10T08:00:00Z",
        counts: {},
        recent: [],
      }),
      ...[
        "/subscriptions",
        "/documents",
        "/partners",
        "/workgroups",
        "/retrypolicies",
        "/audit",
      ].map((p) => none(p)),
    ],
  });

describe("a gateway taking signed calls", () => {
  it("shows its signature settings and how a caller signs, and nothing about keys", async () => {
    openGateway(SIGNED);

    expect(await screen.findByLabelText("Signature header")).toHaveValue(
      "X-Hook-Signature",
    );
    expect(screen.getByLabelText("Signature format")).toHaveValue(
      "TimestampedHmacSha256",
    );
    expect(screen.getByLabelText("Secret property")).toHaveValue(
      "WebhookSecret",
    );
    expect(screen.getByLabelText("Tolerance (seconds)")).toHaveAttribute(
      "placeholder",
      "300",
    );

    expect(
      screen.getByText(/X-Hook-Signature: t=<unix seconds>,v1=/),
    ).toBeVisible();
    expect(screen.getByText(/API keys and tokens are refused/)).toBeVisible();
    expect(screen.queryByLabelText("Ways to send the key")).toBeNull();
    expect(screen.queryByLabelText("Key header")).toBeNull();
  });

  it("hides the tolerance for a plain signature, which carries no time", async () => {
    const { user } = openGateway(SIGNED);

    await user.selectOptions(
      await screen.findByLabelText("Signature format"),
      "HmacSha256",
    );

    expect(screen.queryByLabelText("Tolerance (seconds)")).toBeNull();
    expect(
      screen.getByText(
        /X-Hook-Signature: <hex or base64 HMAC-SHA256 of the body>/,
      ),
    ).toBeVisible();
  });

  it("drops a tolerance left behind when switching to the plain format, so it can't block the save", async () => {
    let sent: Record<string, unknown> | null = null;
    const { user } = openGateway(SIGNED, (body) => (sent = body));

    await user.type(await screen.findByLabelText("Tolerance (seconds)"), "0");
    expect(
      screen.getAllByText("Between 1 second and a day.").length,
    ).toBeGreaterThan(0);
    await user.selectOptions(
      screen.getByLabelText("Signature format"),
      "HmacSha256",
    );
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    await expect.poll(() => sent).not.toBeNull();
    expect(
      (sent!.authentication as Record<string, unknown>)
        .signatureToleranceSeconds,
    ).toBeNull();
  });

  it("won't save a signed gateway missing its header or secret property", async () => {
    let sent: Record<string, unknown> | null = null;
    const { user } = openGateway(KEYS, (body) => (sent = body));

    await user.selectOptions(
      await screen.findByLabelText("Partners send"),
      "Signature",
    );
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    expect(sent).toBeNull();
    expect(
      screen.getAllByText(/Required: the header the signature comes in/).length,
    ).toBeGreaterThan(0);
    expect(
      screen.getByText(
        /Required: the partner property holding the shared secret/,
      ),
    ).toBeVisible();
  });

  it("moves a key gateway to signed calls, warning that keys stop working", async () => {
    let sent: Record<string, unknown> | null = null;
    const { user } = openGateway(KEYS, (body) => (sent = body));

    await user.selectOptions(
      await screen.findByLabelText("Partners send"),
      "Signature",
    );
    await user.type(
      screen.getByLabelText("Signature header"),
      "X-Hook-Signature",
    );
    await user.type(screen.getByLabelText("Secret property"), "WebhookSecret");
    await user.type(screen.getByLabelText("Tolerance (seconds)"), "600");
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    const confirm = within(
      await screen.findByRole("dialog", {
        name: "Change how partners authenticate?",
      }),
    );
    expect(
      confirm.getByText(
        /Only calls signed with an attached partner's secret are accepted/,
      ),
    ).toBeVisible();
    await user.click(confirm.getByRole("button", { name: "Save changes" }));

    await expect.poll(() => sent).not.toBeNull();
    expect(sent!.authentication).toEqual({
      method: "Signature",
      keyHeader: "",
      issuer: "",
      audience: "",
      partnerClaim: "",
      signatureHeader: "X-Hook-Signature",
      signatureScheme: "TimestampedHmacSha256",
      signatureSecretProperty: "WebhookSecret",
      signatureToleranceSeconds: 600,
    });
  });

  it("keeps sending a key gateway's authentication as it always did", async () => {
    let sent: Record<string, unknown> | null = null;
    const { user } = openGateway(KEYS, (body) => (sent = body));

    await user.type(await screen.findByLabelText("Key header"), "X-Api-Key");
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    const confirm = screen.queryByRole("dialog");
    if (confirm)
      await user.click(
        within(confirm).getByRole("button", { name: "Save changes" }),
      );

    await expect.poll(() => sent).not.toBeNull();
    expect(Object.keys(sent!.authentication as object)).not.toContain(
      "signatureHeader",
    );
  });
});
