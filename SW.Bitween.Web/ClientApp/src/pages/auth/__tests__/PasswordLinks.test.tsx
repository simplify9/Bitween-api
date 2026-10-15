import { screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";

/**
 * The pages invite and reset emails lead to, and the one that asks for a reset. All of them are
 * used signed out. That the links work, once, against a real mail server is AccountEmailTests.
 */
const invalidLink = () =>
  HttpResponse.json({ LINK_INVALID: ["This link has expired or has already been used. Ask for a new one."] }, { status: 400 });

describe("forgot password", () => {
  it("is offered on the sign-in page only when this Bitween can email a reset", async () => {
    renderApp("/login", { as: null, config: { passwordResetAvailable: true } });
    expect(await screen.findByRole("link", { name: "Forgot password?" })).toBeVisible();
  });

  it("is not offered without a mail server", async () => {
    renderApp("/login", { as: null });
    expect(await screen.findByRole("button", { name: "Sign in" })).toBeVisible();
    expect(screen.queryByRole("link", { name: "Forgot password?" })).not.toBeInTheDocument();
  });

  it("asks for a link and says to check the inbox, whatever the address", async () => {
    let sent: unknown;
    const { user } = renderApp("/forgot-password", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/forgotPassword"), async ({ request }) => {
          sent = await request.json();
          return new HttpResponse(null, { status: 204 });
        }),
      ],
    });

    await user.type(await screen.findByRole("textbox", { name: "Email" }), "jane@acme.test");
    await user.click(screen.getByRole("button", { name: "Send reset link" }));

    expect(await screen.findByRole("heading", { name: "Check your email" })).toBeVisible();
    // "If it belongs to a member": the page can't say whether it does, because the server won't.
    expect(screen.getByRole("status")).toHaveTextContent("If jane@acme.test belongs to a member");
    expect(sent).toEqual({ email: "jane@acme.test" });
  });
});

describe("a password link", () => {
  it("greets an invited member, sets their password and sends them to sign in", async () => {
    let used: unknown;
    const { user, router } = renderApp("/set-password?token=t0k3n", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/passwordLink"), () =>
          HttpResponse.json({ purpose: "Invite", name: "Jane Cooper", email: "jane@acme.test", expiresOn: "2026-10-17T10:00:00Z" }),
        ),
        http.post(apiPath("/accounts/usePasswordLink"), async ({ request }) => {
          used = await request.json();
          return HttpResponse.json({ email: "jane@acme.test" });
        }),
      ],
    });

    expect(await screen.findByRole("heading", { name: "Welcome, Jane Cooper" })).toBeVisible();
    await user.type(screen.getByLabelText("New password"), "Chosen-by-me-1");
    await user.type(screen.getByLabelText("Confirm password"), "Chosen-by-me-1");
    await user.click(screen.getByRole("button", { name: "Set password" }));

    await expect.poll(() => router.state.location.pathname).toBe("/login");
    expect(used).toEqual({ token: "t0k3n", password: "Chosen-by-me-1" });
    expect(await screen.findByRole("status")).toHaveTextContent("Your password is set. Sign in with it.");
    expect(screen.getByRole("textbox", { name: "Email" })).toHaveValue("jane@acme.test");
  });

  it("checks the two passwords match before asking the server", async () => {
    let calls = 0;
    const { user } = renderApp("/set-password?token=t0k3n", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/passwordLink"), () =>
          HttpResponse.json({ purpose: "PasswordReset", name: "Jane", email: "jane@acme.test", expiresOn: "2026-10-17T10:00:00Z" }),
        ),
        http.post(apiPath("/accounts/usePasswordLink"), () => {
          calls++;
          return HttpResponse.json({ email: "jane@acme.test" });
        }),
      ],
    });

    expect(await screen.findByRole("heading", { name: "Choose a new password" })).toBeVisible();
    await user.type(screen.getByLabelText("New password"), "Chosen-by-me-1");
    await user.type(screen.getByLabelText("Confirm password"), "Chosen-by-me-2");
    await user.click(screen.getByRole("button", { name: "Set password" }));

    expect(await screen.findByText("The passwords don't match.")).toBeVisible();
    expect(calls).toBe(0);
  });

  it("that can't be checked right now says so, and offers to try again rather than a new link", async () => {
    let calls = 0;
    const { user } = renderApp("/set-password?token=t0k3n", {
      as: null,
      handlers: [
        http.post(apiPath("/accounts/passwordLink"), () => {
          calls++;
          return calls === 1
            ? HttpResponse.json({ type: "error", title: "Service unavailable", status: 503 }, { status: 503 })
            : HttpResponse.json({ purpose: "Invite", name: "Jane", email: "jane@acme.test", expiresOn: "2026-10-17T10:00:00Z" });
        }),
      ],
    });

    expect(await screen.findByRole("heading", { name: "Couldn't check your link" })).toBeVisible();
    expect(screen.queryByText(/expired or has already been used/)).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(await screen.findByRole("heading", { name: "Welcome, Jane" })).toBeVisible();
  });

  it("that was used or has expired says so, and where to get a new one", async () => {
    renderApp("/set-password?token=spent", {
      as: null,
      config: { passwordResetAvailable: true },
      handlers: [http.post(apiPath("/accounts/passwordLink"), invalidLink)],
    });

    expect(await screen.findByRole("heading", { name: "This link doesn't work" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Forgot password" })).toHaveAttribute("href", "/forgot-password");
  });
});
