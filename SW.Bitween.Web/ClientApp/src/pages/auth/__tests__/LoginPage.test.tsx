import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderApp } from "../../../__tests__/support/renderApp";

/**
 * What the sign-in page offers is driven by the anonymous config endpoint, so each case is just
 * a different answer from it — which is also why these never touch the real setting: flipping
 * `Bitween.DisableEmailPasswordLogin` on a real instance with no Microsoft app locks everyone out.
 */
const passwordField = () => document.querySelector("#login-password");
const microsoftButton = () => screen.queryByRole("button", { name: "Continue with Microsoft" });

describe("the sign-in page", () => {
  it("asks for an email and password by default", async () => {
    renderApp("/login", { as: null });

    expect(await screen.findByRole("button", { name: "Sign in" })).toBeVisible();
    expect(passwordField()).toBeInTheDocument();
  });

  it("hides the password form when sign-in is Microsoft-only, instead of letting it fail", async () => {
    renderApp("/login", {
      as: null,
      config: { disableEmailPasswordLogin: true, msalClientId: "00000000-0000-0000-0000-000000000000" },
    });

    // The backend rejects email/password outright in this mode, so the form must not be offered.
    expect(await screen.findByRole("button", { name: "Continue with Microsoft" })).toBeVisible();
    expect(passwordField()).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sign in" })).not.toBeInTheDocument();
    // With nothing above it, the divider has nothing to divide.
    expect(screen.queryByText("or", { exact: true })).not.toBeInTheDocument();
  });

  it("explains itself when sign-in is Microsoft-only but no Microsoft app is configured", async () => {
    renderApp("/login", { as: null, config: { disableEmailPasswordLogin: true, msalClientId: null } });

    // Both doors are shut. Saying so beats an empty card that looks like a failed page load.
    expect(await screen.findByText(/Microsoft sign-in isn't configured/)).toBeVisible();
    expect(passwordField()).not.toBeInTheDocument();
    expect(microsoftButton()).not.toBeInTheDocument();
  });
});
