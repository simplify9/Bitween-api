import { expect, test, type Page } from "@playwright/test";
import { ADMIN_EMAIL, ADMIN_PASSWORD } from "./helpers";

/**
 * Signing out, in the one way that needs a real browser: two tabs.
 *
 * The session lives in four places — a row in the database, the HttpOnly refresh
 * cookie, the Jwt in localStorage, and React's own copy. Only the last one decides
 * what you see, and each tab has its own. The other ways a sign-out used to leave
 * the app on screen are in src/auth/__tests__/SignOut.test.tsx; this one depends on
 * the browser delivering a `storage` event from one tab to another, which jsdom
 * cannot do.
 */

const submitCredentials = async (page: Page) => {
  await page.fill("#login-email", ADMIN_EMAIL);
  await page.fill("#login-password", ADMIN_PASSWORD);
  await page.getByRole("button", { name: /^Sign in$/ }).click();
};

async function signIn(page: Page) {
  await page.goto("login");
  await submitCredentials(page);
  await page.waitForURL((u) => !u.pathname.endsWith("/login"), { timeout: 15000 });
}

const signOut = async (page: Page) => {
  await page.getByRole("button", { name: "Account menu" }).click();
  await page.getByRole("button", { name: /Sign out/ }).click();
};

/** The signed-in shell: present only while React holds a session. */
const shell = (page: Page) => page.getByRole("button", { name: "Account menu" });

test("signing out in one tab ends the session in the other", async ({ page, context }) => {
  await signIn(page);
  const other = await context.newPage();
  await other.goto("https://localhost:7155/partners");
  await other.waitForTimeout(2000);
  expect(await shell(other).count()).toBe(1);

  await signOut(page);
  await page.waitForURL(/\/login/, { timeout: 5000 });
  // Both credentials are shared by every tab, so the session really has ended here
  // too — this tab just never used to find out until someone pressed refresh.
  await other.waitForURL(/\/login/, { timeout: 8000 });
});
