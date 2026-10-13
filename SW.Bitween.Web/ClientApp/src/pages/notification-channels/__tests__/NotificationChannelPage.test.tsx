import { screen, waitFor, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * A notification channel's own page. Which settings the server accepts, and what each adapter
 * sends, is SW.Bitween.IntegrationTests' business (NotificationChannelTests); this is what the page
 * offers, what it sends, and where it goes afterwards.
 */

const none = { result: [], totalCount: 0 };

/** What every channel page asks for whatever the channel: the adapter catalog and the `{ }` menu's tokens. */
const shared = [
  http.get(apiPath("/adapters/Catalog"), () => HttpResponse.json([])),
  http.get(apiPath("/globaladaptervaluessets"), () => HttpResponse.json(none)),
  http.get(apiPath("/partners"), () => HttpResponse.json(none)),
];

const mailServer = (host: string, from: string) =>
  http.get(apiPath("/settings"), () =>
    HttpResponse.json(
      [
        ["Bitween.MailServer.Host", host],
        ["Bitween.MailServer.From", from],
      ].map(([key, value]) => ({
        key,
        section: "Mail server",
        label: key,
        description: "",
        kind: "string",
        defaultValue: "",
        value,
        secret: false,
        overridden: value !== "",
        hasValue: value !== "",
        editable: true,
        access: "editable",
      })),
    ),
  );

describe("a new notification channel", () => {
  it("offers Email, Teams and Slack first, and an Email channel only asks who to send to", async () => {
    const created: unknown[] = [];
    const { user, router } = renderApp("/settings/notification-channels/new?return=%2Felsewhere%3FdraftKept%3D1", {
      handlers: [
        ...shared,
        mailServer("smtp.acme.test", "bitween@acme.test"),
        http.post(apiPath("/notificationchannels"), async ({ request }) => {
          created.push(await request.json());
          return HttpResponse.json(42);
        }),
      ],
    });

    const types = await screen.findByRole("radiogroup", { name: "How it sends" });
    expect(within(types).getAllByRole("radio").map((r) => r.textContent)).toEqual([
      expect.stringContaining("Email"),
      expect.stringContaining("Microsoft Teams"),
      expect.stringContaining("Slack"),
    ]);
    // Every other adapter is one step away rather than one of the choices.
    expect(screen.getByRole("radio", { name: /Use another adapter/ })).toBeVisible();

    await user.type(screen.getByLabelText("Name"), "Ops email");
    await user.click(within(types).getByRole("radio", { name: /Email/ }));

    // The server is Settings' business; the channel says where it is.
    expect(await screen.findByText("smtp.acme.test")).toBeVisible();
    expect(screen.queryByLabelText(/Host/)).toBeNull();

    await user.type(screen.getByLabelText("To *"), "ops@acme.test");
    await user.click(screen.getByRole("button", { name: "Create channel" }));

    await waitFor(() => expect(router.state.location.pathname).toBe("/elsewhere"));
    expect(created).toEqual([
      { name: "Ops email", handlerId: "NativeEmailMessageHandler", handlerProperties: { To: "ops@acme.test" } },
    ]);
    // Back where the picker was, with the new channel to pick.
    expect(router.state.location.search).toBe("?draftKept=1&pickedChannel=42");
  });

  it("says an Email channel can't send until a mail server is set", async () => {
    const { user } = renderApp("/settings/notification-channels/new", { handlers: [...shared, mailServer("", "")] });

    await user.click(await screen.findByRole("radio", { name: /Email/ }));

    expect(await screen.findByText(/No mail server is set up yet/)).toBeVisible();
    expect(screen.getByRole("link", { name: "Set one in Settings" })).toHaveAttribute(
      "href",
      "/settings?section=Mail%20server",
    );
  });
});

describe("a saved notification channel", () => {
  const teams = {
    id: 5,
    name: "Ops Teams",
    handlerId: "NativeTeamsMessageHandler",
    handlerProperties: { WebhookUrl: "__private__" },
    usedBy: [{ kind: "Subscription", subscriptionId: 10, retryPolicyId: null, name: "Order intake" }],
  };

  const handlers = [
    ...shared,
    http.get(apiPath("/notificationchannels/5"), () => HttpResponse.json(teams)),
    http.get(apiPath("/notifications"), ({ request }) => {
      // Only this channel's sends are asked for.
      expect(new URL(request.url).searchParams.getAll("filter")).toContain("ChannelId:1:5");
      return HttpResponse.json({
        totalCount: 2,
        result: [
          {
            id: 2,
            xchangeId: "x-2",
            notifierName: "Ops Teams",
            success: false,
            suppressed: true,
            exception: null,
            finishedOn: "2026-10-08T10:02:00Z",
            channelId: 5,
            subscriptionId: 11,
            subscriptionName: "Returns",
          },
          {
            id: 1,
            xchangeId: "x-1",
            notifierName: "Retry budget alert",
            success: false,
            suppressed: false,
            exception: "Teams refused the message (404 Not Found).",
            finishedOn: "2026-10-08T10:00:00Z",
            channelId: 5,
            subscriptionId: 10,
            subscriptionName: "Order intake",
          },
        ],
      });
    }),
    http.get(apiPath("/audit"), () => HttpResponse.json(none)),
  ];

  it("keeps its webhook hidden, and lists who uses it and what it has sent", async () => {
    renderApp("/settings/notification-channels/5", { handlers });

    expect(await screen.findByRole("radio", { name: /Microsoft Teams/ })).toHaveAttribute("aria-checked", "true");
    expect(screen.getByText("••••••••")).toBeVisible();
    expect(screen.getByRole("button", { name: "Replace" })).toBeVisible();

    expect(screen.getByRole("link", { name: "Order intake - subscription notifications" })).toHaveAttribute(
      "href",
      "/subscriptions/10",
    );

    const sends = screen.getByRole("heading", { name: "Recent sends" }).closest("section")!;
    const row = await within(sends).findByRole("row", { name: /Order intake/ });
    expect(within(row).getByText("Retry alert")).toBeVisible();
    expect(within(row).getByText("Failed")).toBeVisible();
    // Held back by the quiet window: not sent, and not a failure either.
    const held = within(sends).getByRole("row", { name: /Returns/ });
    expect(within(held).getByText("Held back")).toBeVisible();
    expect(within(held).queryByText("Failed")).toBeNull();
  });

  it("sends a test message and says why it didn't arrive", async () => {
    const { user } = renderApp("/settings/notification-channels/5", {
      handlers: [
        ...handlers,
        http.post(apiPath("/notificationchannels/5/test"), () =>
          HttpResponse.json({ sent: false, error: "Teams refused the message (400 Bad Request)." }),
        ),
      ],
    });

    await user.click(await screen.findByRole("button", { name: /Send a test message/ }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Not sent: Teams refused the message (400 Bad Request).",
    );
  });

  it("won't test unsaved changes, because the test uses the saved channel", async () => {
    const { user } = renderApp("/settings/notification-channels/5", { handlers });

    await user.clear(await screen.findByLabelText("Name"));
    await user.type(screen.getByLabelText("Name"), "Renamed");

    expect(screen.getByRole("button", { name: /Send a test message/ })).toBeDisabled();
    expect(screen.getByText("Unsaved changes")).toBeVisible();
  });
});
