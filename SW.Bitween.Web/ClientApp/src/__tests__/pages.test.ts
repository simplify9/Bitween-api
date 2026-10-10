import { describe, expect, it } from "vitest";
import { PAGES, pageTrail, pathOf, type PageId } from "../pages";
import { NAV_GROUPS } from "../nav";

const ids = Object.keys(PAGES) as PageId[];

describe("the page registry", () => {
  it("gives every page its own path", () => {
    const paths = ids.map((id) => PAGES[id].path);
    expect(new Set(paths).size).toBe(paths.length);
  });

  it("puts every page under a parent whose path starts its own, so a breadcrumb can be filled in", () => {
    for (const id of ids) {
      const parent = PAGES[id].parent;
      if (parent) expect(PAGES[id].path.startsWith(`${PAGES[parent].path}/`), `${id} under ${parent}`).toBe(true);
    }
  });

  it("guards every page in the sidebar with a permission", () => {
    for (const item of NAV_GROUPS.flatMap((g) => g.items)) expect(item.permissions, item.id).not.toHaveLength(0);
  });

  it("walks a page's trail and fills in its path", () => {
    expect(pageTrail("gatewaySubscriptionNew")).toEqual([
      "apiGateways",
      "apiGateway",
      "attachPartner",
      "gatewaySubscriptionNew",
    ]);
    expect(pathOf("apiGateway", { id: "15" })).toBe("/api-gateways/15");
    expect(pathOf("apiGateway")).toBeNull();
  });
});
