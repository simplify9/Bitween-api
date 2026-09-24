// @vitest-environment jsdom
//
// DOMParser is a browser API, which is where this code runs. jsdom gives the unit
// tests the same one rather than a second XML parser that would agree with neither.

import { describe, it, expect } from "vitest";
import {
  describeSample,
  itemShapeAt,
  listPaths,
  parseSample,
  readablePaths,
  type DocumentNode,
} from "../documentTree";
import { scaffoldFromTarget } from "../scaffold";
import { emptyRules } from "../types";

/**
 * The source tree for an XML sample.
 *
 * These expectations are deliberately the same ones `XmlFormatReadTests` asserts on
 * the server, because this tree is what the editor offers and that reader is what the
 * mapping actually gets. A path offered here that resolves to nothing there would be a
 * mapping that looks right and writes nothing. The end-to-end guard is the Playwright
 * test that maps an XML field and reads the previewed output back off the server.
 */
function tree(xml: string): DocumentNode {
  const parsed = parseSample(xml, "xml");
  expect(parsed.error).toBeNull();
  expect(parsed.root).not.toBeNull();
  return parsed.root!;
}

function at(root: DocumentNode, path: string): DocumentNode | undefined {
  if (root.path === path) return root;
  for (const child of root.children) {
    const found = at(child, path);
    if (found) return found;
  }
  return undefined;
}

describe("reading an XML sample into the source tree", () => {
  it("puts the root element in every path", () => {
    const root = tree("<order><ref>A1</ref></order>");

    expect(at(root, "order.ref")?.sample).toBe("A1");
    expect(at(root, "ref")).toBeUndefined();
  });

  it("leaves prefixes out of the path", () => {
    const root = tree(`
      <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
        <s:Body>
          <CreateShipment xmlns="http://www.cargonet.software">
            <request><referencenumber>BSO-01157</referencenumber></request>
          </CreateShipment>
        </s:Body>
      </s:Envelope>`);

    expect(
      at(root, "Envelope.Body.CreateShipment.request.referencenumber")?.sample,
    ).toBe("BSO-01157");
  });

  it("reads an empty element as present and empty", () => {
    const root = tree("<v><a/><b></b><c>x</c></v>");

    expect(at(root, "v.a")?.sample).toBe("");
    expect(at(root, "v.b")?.sample).toBe("");
    expect(at(root, "v.missing")).toBeUndefined();
  });

  it("makes a repeated name a list, named relative to one entry", () => {
    const root = tree(
      "<order><line><sku>A</sku></line><line><sku>B</sku></line></order>",
    );

    const list = at(root, "order.line");
    expect(list?.kind).toBe("list");
    expect(list?.count).toBe(2);

    // `sku`, not `order.line.sku` — that is the path a rule inside the list writes.
    expect(list?.children.map((c) => c.path)).toEqual(["sku"]);
    expect(listPaths(root)).toEqual(["order.line"]);
  });

  it("leaves a name used once as itself, not a list of one", () => {
    // XML cannot say which, and the tree shows the document. Walking it as a list of
    // one is the mapper's job, which is why the server has `SingleValueIsAList`.
    const root = tree("<order><line><sku>A</sku></line></order>");

    expect(at(root, "order.line")?.kind).toBe("object");
    expect(at(root, "order.line.sku")?.sample).toBe("A");
    expect(listPaths(root)).toEqual([]);
  });

  it("shows an attribute with an at sign and hides namespace declarations", () => {
    const root = tree(
      `<label xmlns:x="http://x" type="PDF_A6"><name>front</name></label>`,
    );

    expect(at(root, "label.@type")?.sample).toBe("PDF_A6");
    expect(at(root, "label.name")?.sample).toBe("front");

    // Four of these sit at the top of a real SOAP document; in the tree they would push
    // the fields anyone is looking for off the screen.
    expect(at(root, "label.@xmlns:x")).toBeUndefined();
    expect(readablePaths(root)).toEqual(["label.@type", "label.name"]);
  });

  it("gives an element with an attribute and text a hash-text path", () => {
    const root = tree(`<weight unit="kg">0.940</weight>`);

    expect(at(root, "weight.@unit")?.sample).toBe("kg");
    expect(at(root, "weight.#text")?.sample).toBe("0.940");
  });

  it("keeps a value exactly as the document wrote it", () => {
    const root = tree(`<a><zipCode> 64310</zipCode><weight>0.940</weight></a>`);

    // The leading space is in the real document; there is a `trim` transform for
    // deciding it is wrong, which is not the tree's decision to make.
    expect(at(root, "a.zipCode")?.sample).toBe(" 64310");
    expect(at(root, "a.weight")?.sample).toBe("0.940");
  });

  it("says so when the sample is not XML", () => {
    const parsed = parseSample("<a><b></a>", "xml");

    expect(parsed.root).toBeNull();
    expect(parsed.error).toMatch(/not valid XML/);
  });

  it("shows nothing rather than an error for an empty sample", () => {
    expect(parseSample("   ", "xml")).toEqual({ root: null, error: null });
  });

  it("does not let a base64 payload push the tree off the screen", () => {
    // A label response carries a PDF this way, tens of kilobytes of it.
    const payload = "J".repeat(60_000);
    const root = tree(`<r><skybill>${payload}</skybill><n>7</n></r>`);

    expect(at(root, "r.skybill")?.sample).toBe(payload);
    expect(describeSample(at(root, "r.skybill")?.sample).length).toBeLessThan(30);
    expect(at(root, "r.n")?.sample).toBe("7");
  });
});

describe("the two ends of a mapping read the same XML differently", () => {
  const soap = `<soap:Envelope xmlns:soap="http://s/"><soap:Body><ok>1</ok></soap:Body></soap:Envelope>`;

  it("drops prefixes and declarations for a document being read", () => {
    // A prefix is the partner's to change between one message and the next, so a path
    // that named one would be naming something outside our control.
    const root = parseSample(soap, "xml", "source").root!;

    expect(readablePaths(root)).toEqual(["Envelope.Body.ok"]);
  });

  it("keeps both for a document being written", () => {
    // Here they are the document: the partner's parser may well insist on them, and
    // nothing else in the mapping could supply them.
    const root = parseSample(soap, "xml", "target").root!;

    expect(readablePaths(root)).toEqual([
      "soap:Envelope.@xmlns:soap",
      "soap:Envelope.soap:Body.ok",
    ]);
  });
});

describe("building rules from a sample of an XML output", () => {
  it("fills in the namespaces from the sample, so none are typed by hand", () => {
    const rules = emptyRules();
    rules.targetFormat = "xml";

    const target = parseSample(
      `<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
         <soap:Body>
           <shipping xmlns="http://cxf.shipping.soap.chronopost.fr/">
             <accountNumber>55480501</accountNumber>
           </shipping>
         </soap:Body>
       </soap:Envelope>`,
      "xml",
      "target",
    ).root;

    const tally = scaffoldFromTarget(rules, target, null);
    expect(tally.problem).toBeNull();

    const ruleFor = (target: string) =>
      rules.fields.find((f) => f.target.join(".") === target);

    // A URI is exactly the kind of thing nobody should retype from memory, and it is
    // part of what the document is rather than something a partner's data fills in.
    expect(ruleFor("soap:Envelope.@xmlns:soap")?.from).toEqual({
      kind: "fixed",
      value: "http://schemas.xmlsoap.org/soap/envelope/",
    });
    expect(ruleFor("soap:Envelope.soap:Body.shipping.@xmlns")?.from).toEqual({
      kind: "fixed",
      value: "http://cxf.shipping.soap.chronopost.fr/",
    });

    // Everything else is still a field waiting for a source, as it should be.
    expect(ruleFor("soap:Envelope.soap:Body.shipping.accountNumber")?.from).toEqual({
      kind: "path",
      path: "",
    });
  });

  it("offers a field the first entry left out but a later one has", () => {
    // The failure this stops: `discount` is in the document, the editor never offers
    // it, and the mapping therefore looks complete while dropping it. Optional
    // elements are the normal case in XML — there is no schema in the sample saying
    // which ones every entry carries.
    const root = parseSample(
      `<order>
         <line><sku>A1</sku></line>
         <line><sku>B7</sku><discount>5</discount></line>
       </order>`,
      "xml",
    ).root;

    expect(itemShapeAt(root, "order.line").map((c) => c.key)).toEqual([
      "sku",
      "discount",
    ]);
  });

  it("treats a name repeated in one entry as a list for every entry", () => {
    // One `<tag>` in the first entry and two in the second. The server walks the
    // single occurrence as a list of one, so the shape a rule is written against has
    // to be the list — offering `tag` as a plain field would be offering a path the
    // mapper never reads that way.
    const root = parseSample(
      `<order>
         <line><tag>red</tag></line>
         <line><tag>red</tag><tag>big</tag></line>
       </order>`,
      "xml",
    ).root;

    const tag = itemShapeAt(root, "order.line").find((c) => c.key === "tag");
    expect(tag?.kind).toBe("list");
  });

  it("makes two rules for an element that holds both an attribute and a value", () => {
    // `<weight unit="kg">0.940</weight>` is one element carrying two things: `@unit` for
    // the attribute and `#text` for its own value, the same convention reading uses.
    const rules = emptyRules();
    rules.targetFormat = "xml";

    const tally = scaffoldFromTarget(
      rules,
      parseSample(`<order><weight unit="kg">0</weight></order>`, "xml", "target").root,
      null,
    );

    expect(tally.problem).toBeNull();
    expect(rules.fields.map((f) => f.target.join("."))).toEqual([
      "order.weight.@unit",
      "order.weight.#text",
    ]);
  });

  it("reads a document that contains an element called parsererror", () => {
    // A browser reports a parse failure by handing back a document holding its own
    // complaint, so the name alone cannot be the signal: a partner is entitled to an
    // element called `parsererror`, and refusing it would reject a valid sample.
    const parsed = parseSample("<order><parsererror>none</parsererror></order>", "xml");

    expect(parsed.error).toBeNull();
    expect(readablePaths(parsed.root)).toContain("order.parsererror");
  });

  it("still reports a document that does not parse", () => {
    const parsed = parseSample("<order><line></order>", "xml");

    expect(parsed.root).toBeNull();
    expect(parsed.error).toMatch(/not valid XML/);
  });
});
