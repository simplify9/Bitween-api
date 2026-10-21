import { describe, expect, it } from "vitest";
import {
  filterChips,
  filterSignature,
  idsIn,
  notWith,
  parseBound,
  propertyFilters,
  parseRelativeRange,
  rangeLabel,
  readQuery,
  resolveRange,
  searchSuggestions,
  timeLabel,
  viewSearch,
  withParams,
  withPropertyFilters,
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
      subscriptionIds: [16],
      partnerIds: [3],
      informationTypeIds: [5],
      ids: "a,b",
      receiveAttemptId: 9,
      correlationId: "c-1",
      // How a link named its one property before there could be several.
      properties: [{ key: "orderNo", text: "SO-1", negated: false }],
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
      ["Auto-retry", "is", "scheduled"],
    ]);
  });

  it("leaves status, time and latest to their own controls", () => {
    expect(filterChips(sp("status=failed&range=1h&latest=1"), lookups)).toEqual([]);
  });

  it("says how many ids rather than listing them", () => {
    const [chip] = filterChips(sp("ids=a,b,c"), lookups);
    expect(chip.value).toBe("3 ids");
    expect(chip.remove).toEqual({ ids: null });
  });

  it("asks for a key on its own as 'has this property'", () => {
    const [chip] = filterChips(sp("propertyKey=orderNo"), lookups);
    expect([chip.field, chip.op, chip.value]).toEqual(["orderNo", "is", "present"]);
  });
});

/**
 * A filter can be turned round - "is not", "does not contain" - and Subscription, Partner and
 * Information type can pick several. Each keeps its own parameter, a list meaning "any of", and
 * `not` names the filters turned round, so a link made before either still means what it did.
 */
describe("several values, and filters turned round", () => {
  it("reads a list as any of, and not as the filters turned round", () => {
    const q = readQuery(sp("subscriptionId=16,17&partnerId=3&error=timeout&not=subscription,error"), NOW);
    expect(q).toMatchObject({ subscriptionIds: [16, 17], partnerIds: [3], errorContains: "timeout", not: ["subscription", "error"] });
  });

  it("counts a not only for a filter that is set", () => {
    expect(readQuery(sp("not=subscription,bogus"), NOW).not).toBeUndefined();
  });

  it("drops a filter's not with the filter, so adding it again starts the right way round", () => {
    const removed = withParams(sp("subscriptionId=16&error=x&not=error,subscription"), { error: null });
    expect(removed.get("not")).toBe("subscription");
    expect(withParams(removed, { subscriptionId: null }).has("not")).toBe(false);
  });

  it("turns one filter round and back without touching the others", () => {
    const q = sp("subscriptionId=16&retry=1&not=retry");
    expect(notWith(q, "subscription", true)).toBe("retry,subscription");
    expect(notWith(q, "retry", false)).toBeNull();
  });

  it("reads each kind of filter both ways round", () => {
    const read = (q: string) => filterChips(sp(q), lookups).map((c) => [c.field, c.op, c.value]);
    expect(read("subscriptionId=16,99&not=subscription")).toEqual([["Subscription", "is none of", "2 subscriptions"]]);
    expect(read("subscriptionId=16,99")).toEqual([["Subscription", "is any of", "2 subscriptions"]]);
    expect(read("partnerId=3&not=partner")).toEqual([["Partner", "is not", "Northwind"]]);
    expect(read("propertyKey=orderNo&property=SO-1&not=property")).toEqual([["orderNo", "does not contain", "SO-1"]]);
    expect(read("propertyKey=orderNo&not=property")).toEqual([["orderNo", "is not", "present"]]);
    expect(read("property=SO-1&not=property")).toEqual([["Properties", "do not contain", "SO-1"]]);
    expect(read("error=timeout&not=error")).toEqual([["Error", "does not contain", "timeout"]]);
    expect(read("retry=1&scheduled=1&aggregated=1&not=retry,scheduled,aggregated")).toEqual([
      ["Exchange", "is not", "a retry"],
      ["Auto-retry", "is not", "scheduled"],
      ["Exchange", "is not", "part of an aggregation"],
    ]);
  });

  it("names every pick behind a count on hover", () => {
    const [chip] = filterChips(sp("subscriptionId=16,99"), lookups);
    expect(chip.title).toBe("Push orders, #99");
  });

  it("keeps several promoted properties, each with its own key and its own way round", () => {
    const q = sp("prop=orderNo:1001&prop=!customer:zed&prop=city&property=acme&not=property");
    expect(readQuery(q, NOW).properties).toEqual([
      { key: "orderNo", text: "1001", negated: false },
      { key: "customer", text: "zed", negated: true },
      { key: "city", text: null, negated: false },
      { key: null, text: "acme", negated: true },
    ]);
    expect(filterChips(q, lookups).map((c) => [c.field, c.op, c.value])).toEqual([
      ["orderNo", "contains", "1001"],
      ["customer", "does not contain", "zed"],
      ["city", "is", "present"],
      ["Properties", "do not contain", "acme"],
    ]);
  });

  it("removes or turns round one property and leaves the others", () => {
    const q = sp("prop=orderNo:1001&prop=customer:zed");
    const [orderNo, customer] = filterChips(q, lookups);
    expect(withParams(q, orderNo.remove).getAll("prop")).toEqual(["customer:zed"]);
    expect(withParams(q, customer.turn!.set(true)).getAll("prop")).toEqual(["orderNo:1001", "!customer:zed"]);
    // The text ends at nothing but the end: a colon in it is part of it.
    expect(readQuery(sp("prop=orderNo:SO:1"), NOW).properties).toEqual([{ key: "orderNo", text: "SO:1", negated: false }]);
  });

  it("writes an older link's one property in the new form the first time the properties change", () => {
    const q = sp("propertyKey=orderNo&property=1001&not=property,error&error=x");
    const changes = withPropertyFilters(q, [...propertyFilters(q), { key: "city", text: null, negated: false }]);
    const next = withParams(q, changes);
    expect(next.getAll("prop")).toEqual(["!orderNo:1001", "city"]);
    expect([next.has("propertyKey"), next.has("property"), next.get("not")]).toEqual([false, false, "error"]);
  });

  it("keeps not in a saved view, as one of the filters", () => {
    expect(viewSearch(sp("subscriptionId=16&not=subscription&open=x"))).toBe("subscriptionId=16&not=subscription");
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

  it("reads key:value as a promoted property by name, added beside the ones already set", () => {
    const [first] = searchSuggestions("orderNo: SO-1", sp("prop=city:paris"));
    expect(first.changes).toEqual({ prop: ["city:paris", "orderNo:SO-1"], propertyKey: null, property: null, not: null });
  });
});
