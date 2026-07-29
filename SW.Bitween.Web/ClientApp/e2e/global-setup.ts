import { request } from "@playwright/test";

/**
 * Leaves the database in a known state before the suite runs.
 *
 * A test that fails part-way skips its own cleanup, and the leftovers aren't harmless: a stray
 * account still holding Administrator is enough to make the last-administrator guard pass, which
 * strips the seeded admin's role and fails everything after it. So rather than trusting each test
 * to tidy up, every run starts by purging its own residue and putting the admin's role back.
 */

const API = "https://localhost:7155/bitween/api";
const ADMIN_EMAIL = "admin@Bitween.systems";
const ADMIN_PASSWORD = "Mtm@dmin!2";
/** Configured break-glass credentials — the only way in when the admin has no roles left. */
const BREAK_GLASS = { username: "1", password: "1" };
const ADMINISTRATOR_ROLE_ID = 1;

const TEST_EMAIL = /^pw-.*@example\.test$/;
const TEST_ROLE = /^PW /;
/** The only settings the suite writes to — see the reset below for why this is a list, not "all". */
const TEST_SETTINGS = ["Theme.PrimaryColor", "Theme.TabTitle", "Theme.CompanyName"];

interface Account {
  id: number;
  email: string;
  roles: { id: number; name: string }[] | null;
}

export default async function purgeTestData() {
  const api = await request.newContext({ ignoreHTTPSErrors: true });

  // Prefer the real admin; fall back to break-glass, which works even with no roles at all.
  const login = await api.post(`${API}/accounts/login`, {
    data: { Username: ADMIN_EMAIL, Password: ADMIN_PASSWORD },
  });
  let token: string = login.ok() ? (await login.json()).jwt : "";

  const auth = () => ({ Authorization: `Bearer ${token}` });
  let accounts = await api.get(`${API}/accounts?limit=500`, { headers: auth() });

  if (!accounts.ok()) {
    const su = await api.post(`${API}/login`, { data: BREAK_GLASS });
    if (!su.ok())
      throw new Error(
        "Can't reach the API as an administrator or via break-glass — is the local backend running?",
      );
    token = (await su.json()).jwt;
    accounts = await api.get(`${API}/accounts?limit=500`, { headers: auth() });
  }

  const rows: Account[] = (await accounts.json()).result ?? [];

  // The seeded admin must hold Administrator, or nothing downstream can manage anything.
  const admin = rows.find((a) => a.email.toLowerCase() === ADMIN_EMAIL.toLowerCase());
  if (admin && !(admin.roles ?? []).some((r) => r.id === ADMINISTRATOR_ROLE_ID)) {
    await api.post(`${API}/accounts/${admin.id}/setRoles`, {
      headers: auth(),
      data: { roleIds: [ADMINISTRATOR_ROLE_ID] },
    });
    // Re-read as the repaired admin so the purge below runs with full permissions.
    const relogin = await api.post(`${API}/accounts/login`, {
      data: { Username: ADMIN_EMAIL, Password: ADMIN_PASSWORD },
    });
    if (relogin.ok()) token = (await relogin.json()).jwt;
  }

  for (const account of rows.filter((a) => TEST_EMAIL.test(a.email)))
    await api.post(`${API}/accounts/${account.id}/remove`, { headers: auth(), data: {} });

  const roles = await api.get(`${API}/roles?pageSize=500`, { headers: auth() });
  for (const role of ((await roles.json()).result ?? []) as { id: number; name: string }[])
    if (TEST_ROLE.test(role.name))
      await api.delete(`${API}/roles/${role.id}`, { headers: auth() });

  // Settings tests assert against product defaults, so a value left behind by a failed run would
  // make them fail for the wrong reason. Only the keys the tests actually touch are reset: the
  // Settings table is now the only home for values like the MSAL ids and the Rebex license key —
  // configuration is read once at first boot and ignored after that — so a blanket reset here
  // would destroy real configuration with no way to get it back.
  for (const key of TEST_SETTINGS)
    await api.delete(`${API}/settings/${encodeURIComponent(key)}`, { headers: auth() });

  await api.dispose();
}
