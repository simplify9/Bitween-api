import { describe, expect, it } from "vitest";
import { SEARCHY_RULE, buildListQuery, searchyQueryString } from "../searchQuery";

/**
 * Guards a bug that was invisible from the UI: every multi-word search silently returned
 * nothing, because the space went out form-encoded as "+" and the backend's query-string
 * parser (Uri.UnescapeDataString) leaves "+" as a literal plus.
 */
describe("searchy query strings", () => {
  it("percent-encodes a space rather than form-encoding it", () => {
    const qs = buildListQuery({
      filters: [["Name", SEARCHY_RULE.contains, "Order intake"]],
      offset: 0,
      limit: 25,
    });

    expect(qs).toContain("Order%20intake");
    expect(qs).not.toContain("+");
  });

  it("leaves a literal plus escaped, so the replace can't corrupt a term", () => {
    const params = new URLSearchParams();
    params.append("filter", "Name:4:a + b");

    // "+" survives as %2B; only the space becomes %20.
    expect(searchyQueryString(params)).toBe("filter=Name%3A4%3Aa%20%2B%20b");
  });

  it("still pages the way every list endpoint expects", () => {
    const qs = buildListQuery({ offset: 50, limit: 25 });
    expect(qs).toBe("page=2&size=25");
  });
});
