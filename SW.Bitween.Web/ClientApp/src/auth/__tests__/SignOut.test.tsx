import { screen, waitFor } from "@testing-library/react";
import type { UserEvent } from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { TOKEN_KEY } from "../../api";
import { apiPath, renderApp } from "../../__tests__/support/renderApp";
import { server } from "../../__tests__/support/server";

/**
 * Signing out, and the ways it used to go wrong.
 *
 * The session lives in four places — a row in the database, the HttpOnly refresh
 * cookie, the Jwt in localStorage, and React's own copy. Only the last one decides
 * what you see, and it used to be the one thing a sign-out could fail to clear:
 * `signOut` awaited the server first, so any failure threw before the session was
 * ended and left the whole app on screen with the Jwt already deleted. Every test
 * here is a way that could happen.
 *
 * The one that needs two real tabs — a sign-out in one ending the session in the
 * other — stays in e2e/sign-out.spec.ts.
 */

/** Somewhere every account can be, which asks the API for nothing but the permission catalog. */
const signedIn = () =>
  renderApp("/profile", { handlers: [http.get(apiPath("/permissions"), () => HttpResponse.json([]))] });

const signOut = async (user: UserEvent) => {
  await user.click(await screen.findByRole("button", { name: "Account menu" }));
  await user.click(screen.getByRole("button", { name: /Sign out/ }));
};

const submitCredentials = async (user: UserEvent) => {
  await user.type(screen.getByLabelText("Email"), "admin@test.local");
  await user.type(document.querySelector<HTMLInputElement>("#login-password")!, "correct-horse");
  await user.click(screen.getByRole("button", { name: "Sign in" }));
};

const loginPage = () => screen.findByRole("heading", { name: "Sign in" });

/** The signed-in shell: present only while React holds a session. */
const shell = () => screen.queryByRole("button", { name: "Account menu" });
const ACCOUNT_MENU = '[aria-label="Account menu"]';

/** A sign-in the server accepts. The same endpoint also serves the silent refresh. */
const acceptSignIn = () =>
  server.use(http.post(apiPath("/accounts/login"), () => HttpResponse.json({ jwt: "fresh-token" })));

/**
 * A logout the test answers when it chooses — or never. Records the request, so a test can
 * see it went out, and whether it was cancelled while waiting.
 */
function heldLogout(answer: (request: Request) => Response = () => HttpResponse.json({})) {
  let release!: () => void;
  const released = new Promise<void>((r) => (release = r));
  const state = { request: null as Request | null, landed: false, release };
  server.use(
    http.post(apiPath("/accounts/logout"), async ({ request }) => {
      state.request = request;
      await released;
      state.landed = true;
      return answer(request);
    }),
  );
  return state;
}

describe("signing out", () => {
  it("shows the login page without waiting for the server", async () => {
    const logout = heldLogout();
    const { user, router } = signedIn();

    await signOut(user);

    // Held on purpose: the session ends locally first, so nothing waits on it.
    expect(await loginPage()).toBeVisible();
    expect(router.state.location.pathname).toBe("/login");
    await waitFor(() => expect(logout.request).not.toBeNull());
    expect(logout.landed).toBe(false);
  });

  it("still signs you out when the server refuses", async () => {
    let answered = false;
    server.use(
      http.post(apiPath("/accounts/logout"), () => {
        answered = true;
        return new HttpResponse("boom", { status: 500 });
      }),
    );
    const { user, router } = signedIn();

    await signOut(user);

    expect(await loginPage()).toBeVisible();
    await waitFor(() => expect(answered).toBe(true));
    expect(router.state.location.pathname).toBe("/login");
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
  });

  it("still signs you out when the backend is unreachable", async () => {
    let attempted = false;
    server.use(
      http.post(apiPath("/accounts/logout"), () => {
        attempted = true;
        return HttpResponse.error();
      }),
    );
    const { user, router } = signedIn();

    await signOut(user);

    expect(await loginPage()).toBeVisible();
    await waitFor(() => expect(attempted).toBe(true));
    expect(router.state.location.pathname).toBe("/login");
  });

  it("does not block signing back in when the sign-out never answers", async () => {
    // Never released: the request hangs rather than failing.
    const logout = heldLogout();
    const { user } = signedIn();
    await signOut(user);
    await loginPage();

    // A sign-in cancels the pending sign-out rather than waiting for it. Waiting was
    // the obvious guard against the race in the next test, and would have deadlocked
    // here for as long as the request hung.
    acceptSignIn();
    await submitCredentials(user);

    expect(await screen.findByRole("button", { name: "Account menu" })).toBeVisible();
    expect(logout.request?.signal.aborted).toBe(true);
  });

  it("cannot let a slow sign-out response wipe the session that replaced it", async () => {
    // The logout response carries `Clear-Site-Data: "cache", "cookies", "storage"`, which
    // the browser applies to the whole origin whenever it lands — including over a newer
    // sign-in. Cancelling the request means the response never arrives. jsdom ignores the
    // header, so the mock does what the browser would, and only if the response reaches it.
    const logout = heldLogout((request) => {
      if (!request.signal.aborted) localStorage.clear();
      return HttpResponse.json({}, { headers: { "Clear-Site-Data": '"cache", "cookies", "storage"' } });
    });
    const { user, router } = signedIn();
    await signOut(user);
    await loginPage();
    acceptSignIn();
    await submitCredentials(user);
    await screen.findByRole("button", { name: "Account menu" });

    logout.release(); // the slow response finally comes back
    await waitFor(() => expect(logout.landed).toBe(true));

    expect(router.state.location.pathname).not.toBe("/login");
    expect(localStorage.getItem(TOKEN_KEY)).toBe("fresh-token");
    expect(shell()).toBeInTheDocument();
  });

  it("cannot let a slow session read flash the app back after a sign-out", async () => {
    // Held open so it lands after the sign-out below. It then falls through to the ordinary
    // 200 — the Jwt went out before the sign-out and is still valid — so its result must be
    // discarded on arrival rather than trusted, or the app appears again over a dead session.
    let profileRequested = false;
    let release!: () => void;
    const released = new Promise<void>((r) => (release = r));
    const { router } = renderApp("/partners", {
      handlers: [
        http.get(apiPath("/accounts/profile"), async () => {
          profileRequested = true;
          await released;
        }),
      ],
    });

    // Anything that ever mounts the shell counts, however briefly it stays.
    let everSignedIn = false;
    const watcher = new MutationObserver((records) => {
      for (const node of records.flatMap((r) => [...r.addedNodes]))
        if (node instanceof Element && (node.matches(ACCOUNT_MENU) || node.querySelector(ACCOUNT_MENU)))
          everSignedIn = true;
    });
    watcher.observe(document.body, { childList: true, subtree: true });

    await waitFor(() => expect(profileRequested).toBe(true));
    // A sign-out in another tab, as this one hears it: the key gone, and a `storage` event.
    // Real delivery between two tabs is what the e2e spec's two-tab test covers.
    localStorage.removeItem(TOKEN_KEY);
    window.dispatchEvent(
      new StorageEvent("storage", { key: TOKEN_KEY, oldValue: "test-token", newValue: null, storageArea: localStorage }),
    );
    release();

    // Settled one way or the other, now that the read has landed.
    await waitFor(() => expect(shell() ?? screen.queryByRole("heading", { name: "Sign in" })).not.toBeNull());
    watcher.disconnect();
    expect(everSignedIn).toBe(false);
    expect(router.state.location.pathname).toBe("/login");
    expect(screen.getByRole("heading", { name: "Sign in" })).toBeVisible();
  });

  it("does not need a refresh when the session ended server-side", async () => {
    const { user } = signedIn();
    await screen.findByRole("button", { name: "Account menu" });

    // Exactly what a sign-out elsewhere leaves behind: no cookie, a useless Jwt.
    localStorage.setItem(TOKEN_KEY, "dead");
    const refused = () => new HttpResponse(null, { status: 401 });
    server.use(
      // Everything the partners page reads.
      http.get(apiPath("/partners"), refused),
      http.get(apiPath("/subscriptions"), refused),
      http.get(apiPath("/apigateways"), refused),
      http.get(apiPath("/busgateways"), refused),
      // No cookie, so the silent refresh behind those 401s is refused too.
      http.post(apiPath("/accounts/login"), refused),
    );
    await user.click(screen.getByRole("link", { name: /^Partners$/ }));

    expect(await loginPage()).toBeVisible();
  });
});
