import { describe, expect, it } from "vitest";
import {
  filterChips,
  filterSignature,
  idsIn,
  parseBound,
  parseRelativeRange,
  rangeLabel,
  readQuery,
  resolveRange,
  searchSuggestions,
  timeLabel,
  viewSearch,
  withParams,
} from "../exchangeFilters";

/**
 * The exchanges list keeps every filter in the URL. These are the rules for reading and writing
 * it - above all that a link made before the redesign still selects the same exchanges.
 */

const sp = (q: string) => new URLSearchParams(q);
const NOW = new Date(2026, 9, 11, 14, 30, 0); // 11 Oct 2026, 14:30 local
const lookups = {
  subscriptionName: (id: number) => (id === 16 ? "Push orders" : undefined),
  partnerName: (id: number) => (id === 3 ? "Northwind" : undefined),
  informationTypeName: (id: number) => (id === 5 ? "Purchase order" : undefined),
};

describe("reading an older link", () => {
  it("reads every parameter the dashboard and other pages link in with", () => {
    const q = readQuery(
      sp("status=failed&latest=1&subscriptionId=16&partnerId=3&informationTypeId=5&ids=a,b&receiveAttemptId=9&correlationId=c-1&propertyKey=orderNo&property=SO-1&offset=25"),
      NOW,
    );
    expect(q).toMatchObject({
      status: "failed",
      latest: true,
      subscriptionId: 16,
      partnerId: 3,
      informationTypeId: 5,
      ids: "a,b",
      receiveAttemptId: 9,
      correlationId: "c-1",
      propertyKey: "orderNo",
      property: "SO-1",
      offset: 25,
      limit: 25,
    });
  });

  it("takes a bare date as the whole of that day, in local time", () => {
    const q = readQuery(sp("from=2026-09-01&to=2026-09-02"), NOW);
    expect(q.from).toBe(new Date(2026, 8, 1, 0, 0, 0).toISOString());
    expect(q.to).toBe(new Date(2026, 8, 2, 23, 59, 59).toISOString());
  });

  it("ignores a status it does not know rather than sending it", () => {
    expect(readQuery(sp("status=bogus"), NOW).status).toBeUndefined();
  });
});

describe("time", () => {
  it("resolves a relative range against now, so a live list keeps meaning the last hour", () => {
    expect(readQuery(sp("range=1h"), NOW).from).toBe(new Date(NOW.getTime() - 3_600_000).toISOString());
    expect(readQuery(sp("range=1h"), NOW).to).toBeUndefined();
    // A range wins over from/to left in the URL.
    expect(readQuery(sp("range=15m&from=2020-01-01"), NOW).from).toBe(new Date(NOW.getTime() - 900_000).toISOString());
  });

  it("knows today and yesterday as calendar days", () => {
    expect(resolveRange("today", NOW)).toEqual({ from: new Date(2026, 9, 11) });
    expect(resolveRange("yesterday", NOW)).toEqual({ from: new Date(2026, 9, 10), to: new Date(new Date(2026, 9, 11).getTime() - 1) });
  });

  it("reads typed ranges and names them", () => {
    expect(parseRelativeRange("2h")).toBe("2h");
    expect(parseRelativeRange(" 90 M ")).toBe("90m");
    expect(parseRelativeRange("0h")).toBeNull();
    expect(parseRelativeRange("soon")).toBeNull();
    expect(rangeLabel("2h")).toBe("Last 2 hours");
    expect(rangeLabel("1d")).toBe("Last 1 day");
    expect(rangeLabel("24h")).toBe("Last 24 hours");
  });

  it("says what the time chip covers", () => {
    expect(timeLabel(sp(""))).toBe("Any time");
    expect(timeLabel(sp("range=7d"))).toBe("Last 7 days");
    expect(timeLabel(sp("from=2026-09-01"))).toMatch(/^Since /);
    expect(timeLabel(sp("from=2026-09-01T09:15&to=2026-09-01T10:00"))).toMatch(/09:15 to .*10:00$/);
  });

  it("does not read a malformed bound as a date", () => {
    expect(parseBound("yesterday-ish", "from")).toBeNull();
  });
});

describe("changing the URL", () => {
  it("goes back to the first page when a filter changes, and only then", () => {
    expect(withParams(sp("offset=50&status=failed"), { status: null }).toString()).toBe("");
    expect(withParams(sp("offset=50"), { open: "x" }, { resetOffset: false }).toString()).toBe("offset=50&open=x");
  });

  it("compares two URLs by their filters alone", () => {
    expect(filterSignature(sp("status=failed&open=a&offset=25"))).toBe(filterSignature(sp("status=failed")));
    expect(filterSignature(sp("status=failed"))).not.toBe(filterSignature(sp("status=success")));
    expect(viewSearch(sp("open=a&offset=25&latest=1&status=failed"))).toBe("status=failed&latest=1");
  });
});

describe("chips", () => {
  it("names each filter by what it picked, not by its id", () => {
    const chips = filterChips(sp("subscriptionId=16&partnerId=99&propertyKey=orderNo&property=SO-1&error=timeout&scheduled=1"), lookups);
    expect(chips.map((c) => [c.field, c.op, c.value])).toEqual([
      ["Subscription", "is", "Push orders"],
      // Not loaded, or gone since: the id is all there is to show.
      ["Partner", "is", "#99"],
      ["orderNo", "contains", "SO-1"],
      ["Error", "contains", "timeout"],
      ["Auto-retry scheduled", "", ""],
    ]);
  });

  it("leaves status, time and latest to their own controls", () => {
    expect(filterChips(sp("status=failed&range=1h&latest=1"), lookups)).toEqual([]);
  });

  it("says how many ids rather than listing them", () => {
    const [chip] = filterChips(sp("ids=a,b,c"), lookups);
    expect(chip.value).toBe("3 ids");
    expect(chip.keys).toEqual(["ids"]);
  });

  it("asks for a key on its own as 'has this property'", () => {
    const [chip] = filterChips(sp("propertyKey=orderNo"), lookups);
    expect([chip.field, chip.op, chip.value]).toEqual(["orderNo", "is", "present"]);
  });
});

describe("the search box", () => {
  const id = "3f2a9c1e-0000-4000-8000-000000000001";

  it("opens a pasted exchange id", () => {
    const [first] = searchSuggestions(id);
    expect(first.changes).toEqual({ ids: id, open: id });
    expect(idsIn(id.replace(/-/g, ""))).toEqual([id.replace(/-/g, "")]);
  });

  it("takes several pasted ids as one filter, opening none", () => {
    const [first] = searchSuggestions(`${id}\n${id.replace("1", "2")}`);
    expect(first.label).toBe("2 exchanges");
    expect(first.changes.open).toBeNull();
  });

  it("offers every place a plain value could be", () => {
    expect(searchSuggestions("SO-48213").map((s) => s.key)).toEqual(["property", "correlationId", "error"]);
    expect(searchSuggestions("  ")).toEqual([]);
  });

  it("reads key:value as a promoted property by name", () => {
    const [first] = searchSuggestions("orderNo: SO-1");
    expect(first.changes).toEqual({ propertyKey: "orderNo", property: "SO-1" });
  });
});
