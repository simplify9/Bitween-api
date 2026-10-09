import { describe, expect, it } from "vitest";
import type { AdapterInfo, SubscriptionInfo } from "../../api";
import { matchesSearch, mergeCatalogs, usageByAdapter } from "../adapterInventory";

const adapter = (over: Partial<AdapterInfo>): AdapterInfo => ({
  id: "x",
  kind: "handler",
  label: "X",
  native: false,
  versions: [],
  props: [],
  tags: [],
  currentVersion: null,
  versionHistory: [],
  ...over,
});

describe("mergeCatalogs", () => {
  it("folds an adapter listed under two kinds into one entry with both", () => {
    const both = adapter({ id: "acme.io", label: "Acme IO" });
    const merged = mergeCatalogs({ receiver: [{ ...both, kind: "receiver" }], handler: [both] });

    expect(merged).toHaveLength(1);
    expect(merged[0].kinds).toEqual(["receiver", "handler"]);
  });

  it("lists versions newest first from the catalog, or from bare version files without one", () => {
    const [withCatalog, withoutCatalog] = mergeCatalogs({
      handler: [
        adapter({
          id: "a",
          label: "A",
          currentVersion: "1.1.0",
          versionHistory: [
            { version: "1.0.0", publishedOn: null, publishedBy: null, releaseNotes: null, withdrawn: true, hasSource: false },
            { version: "1.1.0", publishedOn: null, publishedBy: null, releaseNotes: "new", withdrawn: false, hasSource: false },
          ],
        }),
        adapter({ id: "b", label: "B", versions: ["0.9.0", "1.0.0"] }),
      ],
    });

    expect(withCatalog.versions.map((v) => v.version)).toEqual(["1.1.0", "1.0.0"]);
    expect(withCatalog.hasCatalog).toBe(true);
    expect(withoutCatalog.versions.map((v) => v.version)).toEqual(["1.0.0", "0.9.0"]);
    expect(withoutCatalog.currentVersion).toBe("1.0.0");
    expect(withoutCatalog.hasCatalog).toBe(false);
  });

  it("gives built-in adapters no current version", () => {
    const [native] = mergeCatalogs({ handler: [adapter({ id: "NativeSmtpHandler", native: true })] });
    expect(native.currentVersion).toBeNull();
  });
});

describe("usageByAdapter", () => {
  const sub = (id: number, uses: SubscriptionInfo["adapterUses"]) =>
    ({ id, name: `S${id}`, adapterUses: uses }) as SubscriptionInfo;

  it("counts subscriptions once per adapter, and pins per version", () => {
    const usage = usageByAdapter([
      sub(1, [{ kind: "handler", adapterId: "Acme", version: "1.0.0" }]),
      sub(2, [
        { kind: "receiver", adapterId: "acme", version: null },
        { kind: "handler", adapterId: "acme", version: null },
      ]),
      sub(3, [{ kind: "handler", adapterId: "other", version: null }]),
    ]);

    const acme = usage.get("acme")!;
    expect(acme.subscriptions.map((s) => s.id)).toEqual([1, 2]);
    expect(acme.pinned).toEqual({ "1.0.0": 1 });
    expect(acme.followingCurrent).toBe(2);
  });
});

describe("matchesSearch", () => {
  it("matches the name, id, publisher and tags, ignoring case", () => {
    const [a] = mergeCatalogs({
      handler: [adapter({ id: "acme.orders", label: "Orders", publisher: "Acme Ltd", tags: ["edi"] })],
    });
    for (const q of ["orders", "ACME.ORD", "acme ltd", "EDI", ""]) expect(matchesSearch(a, q)).toBe(true);
    expect(matchesSearch(a, "nothing")).toBe(false);
  });
});
