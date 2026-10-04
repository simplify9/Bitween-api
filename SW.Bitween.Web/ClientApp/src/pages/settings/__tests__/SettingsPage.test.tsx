import { cleanup, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, describe, expect, it } from "vitest";
import type { RetentionStatus, SettingRow } from "../../../api";
import { apiPath, renderApp } from "../../../__tests__/support/renderApp";
import { server } from "../../../__tests__/support/server";
import { settingsDraft } from "../../../lib/settingsDraft";

/**
 * Which sections exist, which rows are environment-owned and which are editable are facts about
 * the backend's catalog, pinned by SettingsCatalogTests. What's left here is how the page renders
 * each kind of row it is sent.
 */
const DEFAULT_CRON = "0 * * * * ?";

/** A stored, changeable row as Settings.Get sends one: untouched, so still at its default. */
const editable = (row: Pick<SettingRow, "key" | "section" | "label" | "kind" | "defaultValue">): SettingRow => ({
  description: "",
  value: row.defaultValue,
  secret: false,
  overridden: false,
  hasValue: row.defaultValue !== "",
  editable: true,
  access: "editable",
  ...row,
});

/** An environment row as Settings.Get sends one: no default, nothing to reset, never writable. */
const environment = (
  row: Pick<SettingRow, "key" | "section" | "label" | "kind" | "access" | "value" | "hasValue">,
): SettingRow => ({
  description: "",
  defaultValue: "",
  secret: false,
  overridden: false,
  editable: false,
  ...row,
});

/** A few rows from each section, in catalog order — which is the order the page lists them in. */
const ROWS: SettingRow[] = [
  editable({
    key: "Bitween.DocumentPrefix",
    section: "Documents & storage",
    label: "Exchange files prefix",
    kind: "string",
    defaultValue: "temp30/Bitweendocs",
  }),
  editable({
    key: "Bitween.ExchangeRetentionDays",
    section: "Documents & storage",
    label: "Keep exchanges (days)",
    kind: "number",
    defaultValue: "0",
  }),
  editable({
    key: "Bitween.JwtExpiryMinutes",
    section: "API behavior",
    label: "Sign-in session length (minutes)",
    kind: "number",
    defaultValue: "60",
  }),
  editable({
    key: "Bitween.MsalClientId",
    section: "Single sign-on (Microsoft)",
    label: "Azure AD client ID",
    kind: "string",
    defaultValue: "",
  }),
  editable({
    key: "Bitween.DisableEmailPasswordLogin",
    section: "Single sign-on (Microsoft)",
    label: "Microsoft sign-in only",
    kind: "boolean",
    defaultValue: "false",
  }),
  environment({
    key: "Bitween.AdapterPath",
    section: "Adapters",
    label: "Custom adapter path",
    kind: "string",
    access: "readonly",
    value: "adapters",
    hasValue: true,
  }),
  editable({
    key: "Bitween.RetryJobCron",
    section: "Reliability & jobs",
    label: "Retry poll schedule",
    kind: "string",
    defaultValue: DEFAULT_CRON,
  }),
  environment({
    key: "Bitween.QueuePrefix",
    section: "Messaging",
    label: "Queue name prefix",
    kind: "string",
    access: "readonly",
    value: "bitween",
    hasValue: true,
  }),
  environment({
    key: "Bitween.UseAzureManagedIdentity",
    section: "Database",
    label: "Use Azure managed identity",
    kind: "boolean",
    access: "readonly",
    value: "false",
    hasValue: true,
  }),
  // A presence row's value never leaves the server: Settings.Get sends null either way.
  environment({
    key: "Bitween.AzureManagedIdentityClientId",
    section: "Database",
    label: "Managed identity client ID",
    kind: "string",
    access: "presence",
    value: null,
    hasValue: false,
  }),
  environment({
    key: "Bitween.SettingsEncryptionKey",
    section: "Security",
    label: "Settings encryption key",
    kind: "string",
    access: "presence",
    value: null,
    hasValue: true,
  }),
  editable({
    key: "Theme.PrimaryColor",
    section: "Brand & theme",
    label: "Primary color",
    kind: "color",
    defaultValue: "#e3311d",
  }),
];

/** What Retention.Get sends for a bucket with the temp rules and exchanges kept for ever. */
const retention = (overrides: Partial<RetentionStatus> = {}): RetentionStatus => ({
  storage: {
    provider: "S3",
    bucket: "bitween",
    problem: null,
    rules: [
      { id: "temp7", prefix: "temp7/", days: 7, enabled: true },
      { id: "temp30", prefix: "temp30/", days: 30, enabled: true },
    ],
  },
  files: { prefix: "temp30/Bitweendocs", days: 30, rulePrefix: "temp30/" },
  legacyFiles: null,
  archive: { prefix: "archive/Bitweendocs", days: null, rulePrefix: null },
  exchanges: {
    retentionDays: 0,
    archive: true,
    cron: "0 0 4 * * ?",
    nextRun: "2026-10-01T04:00:00Z",
    dueNow: null,
    withoutFiles: 12,
    countCap: 100000,
    oldest: "2026-01-01T00:00:00Z",
  },
  notices: [{ level: "info", code: "KEEP_FOREVER", message: "Exchanges are kept for ever: nothing removes them from the database." }],
  ...overrides,
});

const pageHandlers = [
  http.get(apiPath("/settings"), () => HttpResponse.json(ROWS)),
  http.get(apiPath("/retention"), () => HttpResponse.json(retention())),
  // The history card underneath; nothing has changed on this instance yet.
  http.get(apiPath("/audit"), () => HttpResponse.json({ result: [], totalCount: 0 })),
];

const openSettings = (section?: string) =>
  renderApp(section ? `/settings?section=${encodeURIComponent(section)}` : "/settings", {
    handlers: pageHandlers,
  });

/** Sections are real links — a section is a URL you can paste into a ticket — not buttons. */
const sectionNav = async () => within(await screen.findByRole("navigation", { name: "Settings sections" }));

// The draft is module state mirrored to sessionStorage, which setup.ts doesn't clear — so a test
// that fails with an edit staged would otherwise hand it to the next one. Unmounted first, so
// clearing it doesn't re-render a page nobody is looking at any more.
afterEach(() => {
  cleanup();
  settingsDraft.discardAll();
});

describe("the settings page", () => {
  it("lists a section per catalog section, in catalog order, with no restart-required rows", async () => {
    openSettings();

    const links = (await sectionNav()).getAllByRole("link");
    expect(links.map((l) => l.textContent)).toEqual([
      "Documents & storage",
      "API behavior",
      "Single sign-on (Microsoft)",
      "Adapters",
      "Reliability & jobs",
      "Messaging",
      "Database",
      "Security",
      "Brand & theme",
    ]);

    // Nothing carries a restart badge: a setting that couldn't take effect immediately is shown
    // as an environment value instead of being offered as an edit that needs a restart to land.
    expect(screen.queryByText("Restart", { exact: true })).not.toBeInTheDocument();
  });

  it("shows environment settings but doesn't offer them as edits", async () => {
    const { user } = openSettings();
    await user.click((await sectionNav()).getByRole("link", { name: "Database" }));

    // A read-only row renders its value as text — there's no control carrying its label…
    expect(await screen.findByText("Use Azure managed identity")).toBeVisible();
    expect(screen.getByText("Off", { exact: true })).toBeVisible();
    expect(screen.queryByRole("textbox", { name: "Use Azure managed identity" })).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();

    // …and a presence row reports only whether a value is set, never the value itself.
    expect(screen.getByText("Not set", { exact: true })).toBeVisible();
    expect(screen.queryByRole("textbox", { name: "Managed identity client ID" })).not.toBeInTheDocument();

    // Neither kind can be reset, because neither is stored.
    expect(screen.queryByRole("button", { name: "Reset to default" })).not.toBeInTheDocument();
    expect(screen.getAllByText("Environment")).toHaveLength(2);
  });

  it("offers Microsoft-only sign-in as a toggle, not an environment value", async () => {
    openSettings("Single sign-on (Microsoft)");

    // It applies per request — the Login handler and the config endpoint both read it live — so it
    // belongs in the catalog as an edit rather than a read-only environment row.
    const toggle = await screen.findByRole("checkbox", { name: "Off" });
    expect(toggle).toBeEnabled();
    expect(toggle).not.toBeChecked();
    expect(screen.getByText("Microsoft sign-in only")).toBeVisible();
    expect(screen.queryByText("Environment")).not.toBeInTheDocument();
  });

  it("says what the retention settings do, and says it again before a change is saved", async () => {
    let previewed: unknown;
    let saved: unknown;
    const warning = "Exchanges older than 30 days are listed without their files: they can't be opened or retried.";
    server.use(
      http.post(apiPath("/retention/preview"), async ({ request }) => {
        previewed = await request.json();
        return HttpResponse.json(
          retention({
            exchanges: { ...retention().exchanges, retentionDays: 90, dueNow: 3 },
            notices: [{ level: "warning", code: "OUTLIVE_FILES", message: warning }],
          }),
        );
      }),
      http.post(apiPath("/settings/Bitween.ExchangeRetentionDays"), async ({ request }) => {
        saved = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const { user } = openSettings("Documents & storage");

    // The saved state, and the bucket's own rules with the one exchange files fall under marked.
    expect(await screen.findByText("What these settings do")).toBeVisible();
    expect(screen.getByText(/nothing removes them from the database/)).toBeVisible();
    const rules = within(screen.getByRole("table"));
    expect(rules.getByText("temp30/")).toBeVisible();
    expect(rules.getByText("Exchange files")).toBeVisible();

    // A staged change is previewed straight away, before anything is saved…
    const daysBox = screen.getByRole("spinbutton", { name: "Keep exchanges (days)" });
    await user.clear(daysBox);
    await user.type(daysBox, "90");
    await user.tab();
    expect(await screen.findByText("What your unsaved changes would do")).toBeVisible();
    expect(await screen.findByText(warning)).toBeVisible();
    expect(previewed).toMatchObject({ exchangeRetentionDays: 90, documentPrefix: "temp30/Bitweendocs" });

    // …and saving asks first, with the same consequences, rather than writing on the spot.
    await user.click(screen.getByRole("button", { name: "Save changes" }));
    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText("Save the retention changes?")).toBeVisible();
    expect(dialog.getByText(warning)).toBeVisible();
    expect(saved).toBeUndefined();

    await user.click(dialog.getByRole("button", { name: "Save changes" }));
    await screen.findByText("What these settings do");
    expect(saved).toEqual({ value: "90" });
  });

  it("won't save a retention change until the preview answers for that change", async () => {
    let answer!: () => void;
    const answered = new Promise<void>((resolve) => (answer = resolve));
    const warning = "Exchanges older than 30 days are listed without their files: they can't be opened or retried.";
    server.use(
      http.post(apiPath("/retention/preview"), async () => {
        await answered;
        return HttpResponse.json(retention({ notices: [{ level: "warning", code: "OUTLIVE_FILES", message: warning }] }));
      }),
    );
    const { user } = openSettings("Documents & storage");
    expect(await screen.findByText("What these settings do")).toBeVisible();

    const daysBox = screen.getByRole("spinbutton", { name: "Keep exchanges (days)" });
    await user.clear(daysBox);
    await user.type(daysBox, "90");
    await user.tab();
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    // The saved settings' notices aren't what the change does, so nothing is shown and nothing can be saved yet.
    const dialog = within(await screen.findByRole("dialog"));
    expect(dialog.getByText("Working out what they'd do…")).toBeVisible();
    expect(dialog.getByRole("button", { name: "Save changes" })).toBeDisabled();

    answer();
    expect(await dialog.findByText(warning)).toBeVisible();
    expect(dialog.getByRole("button", { name: "Save changes" })).toBeEnabled();
  });

  it("keeps an invalid retry schedule as an unsaved draft when the backend refuses it", async () => {
    let posted: unknown;
    server.use(
      http.post(apiPath("/settings/Bitween.RetryJobCron"), async ({ request }) => {
        posted = await request.json();
        // What Settings.Update throws for a bad expression, as the framework serializes it.
        return HttpResponse.json(
          {
            SETTING_INVALID_VALUE: ["Retry poll schedule: 'not a cron' is not a valid cron expression."],
          },
          { status: 400 },
        );
      }),
    );
    const { user } = openSettings("Reliability & jobs");

    const cron = await screen.findByRole("textbox", { name: "Retry poll schedule" });
    expect(cron).toHaveValue(DEFAULT_CRON);

    // The backend validates the expression before storing it, because a bad one would break the
    // startup job seeding — so a rejected save leaves the draft dirty rather than silently passing.
    await user.clear(cron);
    await user.type(cron, "not a cron");
    await user.tab();
    await user.click(screen.getByRole("button", { name: "Save changes" }));

    expect(await screen.findByText(/not a valid cron expression/)).toBeVisible();
    expect(posted).toEqual({ value: "not a cron" });
    expect(cron).toHaveValue("not a cron");
    expect(screen.getByText("Unsaved", { exact: true })).toBeVisible();

    await user.click(screen.getByRole("button", { name: "Discard" }));
    expect(cron).toHaveValue(DEFAULT_CRON);
    expect(screen.queryByText("Unsaved", { exact: true })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save changes" })).not.toBeInTheDocument();
  });
});
