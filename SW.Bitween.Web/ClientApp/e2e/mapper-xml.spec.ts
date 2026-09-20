import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";
import {
  addFixedRule,
  addList,
  addListField,
  addPathRule,
  buildFromSample,
  createSubscription,
  expectPreview,
  openMapper,
  saveAndReload,
  suggestionsFor,
  withFormats,
} from "./mapperHelpers";

/**
 * XML, end to end through the real server.
 *
 * The point of running these in a browser rather than as unit tests: the source tree is
 * built in TypeScript and the mapping is run in C#, by two readers written separately.
 * A path the tree offers that the server's reader does not produce would be a mapping
 * that looks right in the editor and quietly writes nothing. Every test here reads a
 * path out of the tree and then asserts on a preview the server produced, so the two
 * cannot drift apart without one of them failing.
 *
 * The documents are the shapes real carriers send — SOAP requests and responses — with
 * the credentials replaced.
 */

test.beforeEach(async ({ page }) => {
  await signInAsAdmin(page);
});

/** A carrier's shipping request: prefixed envelope, default namespace, un-declared subtree. */
const SOAP_REQUEST = `<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
  <s:Header>
    <h:UserCredentials xmlns:h="http://www.cargonet.software">
      <userid>ACCOUNT</userid>
      <password>REPLACED</password>
    </h:UserCredentials>
  </s:Header>
  <s:Body>
    <shipping xmlns="http://cxf.shipping.soap.chronopost.fr/">
      <headerValue xmlns="">
        <accountNumber>55480501</accountNumber>
        <idEmit>CHRFR</idEmit>
      </headerValue>
      <shipperValue xmlns="">
        <shipperCity>LYON</shipperCity>
        <shipperAdress2/>
      </shipperValue>
      <weight unit="kg">0.940</weight>
      <shippingdate>04.09.2026</shippingdate>
    </shipping>
  </s:Body>
</s:Envelope>`;

/** Opens the editor on a subscription with an XML source sample already in it. */
async function openWithXml(page: import("@playwright/test").Page, sample = SOAP_REQUEST) {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await withFormats(page, () => page.getByLabel("From format").selectOption("xml"));
  await page.getByRole("textbox", { name: "Sample source document" }).fill(sample);
  return subscriptionId;
}

test("the paths the source tree offers are the ones the server can read", async ({ page }) => {
  await openWithXml(page);

  await page.getByRole("button", { name: "Add a field", exact: true }).click();
  await page.getByRole("textbox", { name: "Output field name" }).last().fill("account");

  // Prefixes are not in a path: the same namespace turns up as `s:` in one message and
  // `soap:` in the next, and `xmlns=""` puts a whole subtree back into no namespace.
  const field = page.getByLabel("Source field", { exact: true }).last();
  const offered = await suggestionsFor(page, field);
  expect(offered).toContain("Envelope.Body.shipping.headerValue.accountNumber");
  expect(offered).toContain("Envelope.Header.UserCredentials.userid");
  expect(offered.some((p) => p.includes("s:") || p.includes("xmlns"))).toBe(false);

  await field.fill("Envelope.Body.shipping.headerValue.accountNumber");

  // Read by the server, not by the editor — which is what makes this a real check.
  await expectPreview(page, '"account": "55480501"');
});

test("an empty element reads as empty rather than as missing", async ({ page }) => {
  await openWithXml(page);

  await addPathRule(page, "address2", "Envelope.Body.shipping.shipperValue.shipperAdress2");
  await addPathRule(page, "city", "Envelope.Body.shipping.shipperValue.shipperCity");

  // `<shipperAdress2/>` is all over a real request. The element is there and its text is
  // empty, and the mapping has to be able to tell that from an element nobody sent.
  await expectPreview(page, '"address2": ""');
  await expectPreview(page, '"city": "LYON"');
});

test("an order with a single line still maps that line", async ({ page }) => {
  // The quietest bug in XML mapping: one <line> is the same document as no list at all,
  // so the mapping reports success and the only line is gone. It surfaces in production,
  // on the one order that happened to have a single item.
  const one = `<order><ref>A1</ref><line><sku>ONLY</sku></line></order>`;
  const three = `<order><ref>A1</ref>
    <line><sku>X</sku></line><line><sku>Y</sku></line><line><sku>Z</sku></line></order>`;

  await openWithXml(page, three);

  const list = await addList(page, "lines", "order.line");
  await addListField(list, "lines", "code", "sku");
  await expectPreview(page, /"code": "X"[\s\S]*"code": "Y"[\s\S]*"code": "Z"/);

  // The same mapping, against a document with one of them.
  await page.getByRole("textbox", { name: "Sample source document" }).fill(one);
  await expectPreview(page, '"code": "ONLY"');
});

test("a mapping that writes XML takes its namespaces from the sample of the output", async ({
  page,
}) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await withFormats(page, async () => {
    await page.getByLabel("From format").selectOption("xml");
    await page.getByLabel("To format").selectOption("xml");
  });
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SOAP_REQUEST);

  await buildFromSample(
    page,
    `<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
       <soap:Body><ack><account>0</account></ack></soap:Body>
     </soap:Envelope>`,
  );

  // Nothing about a namespace was typed: the sample carried it, the scaffold made it a
  // fixed value, and the writer put the prefix back where the sample had it.
  await page
    .getByLabel("Source field", { exact: true })
    .last()
    .fill("Envelope.Body.shipping.headerValue.accountNumber");

  await expectPreview(page, 'xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"');
  await expectPreview(page, "<account>55480501</account>");
  await expectPreview(page, "<soap:Body>");

  // And it is all still there after a round trip through the database.
  await saveAndReload(page);
  await expectPreview(page, 'xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"');
});

test("a shape XML cannot hold is refused with a reason, not a broken document", async ({
  page,
}) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await withFormats(page, async () => {
    await page.getByLabel("From format").selectOption("xml");
    await page.getByLabel("To format").selectOption("xml");
  });
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SOAP_REQUEST);

  // JSON writes as many top-level keys as it likes; XML has exactly one root element.
  await addFixedRule(page, "first", "1");
  await addFixedRule(page, "second", "2");

  await expect(page.getByText(/exactly one root element/)).toBeVisible({ timeout: 15000 });
});

test("a source sample that is not XML says so instead of showing an empty tree", async ({
  page,
}) => {
  await openWithXml(page, "<order><ref>A1</order>");

  await expect(page.getByText(/not valid XML/i).first()).toBeVisible({ timeout: 15000 });
});

test("an element that carries both an attribute and a value maps as two rules", async ({
  page,
}) => {
  // `<weight unit="kg">0.940</weight>` is one element holding two separate things, so it
  // is two rules: `@unit` for the attribute and `#text` for the element's own value. The
  // same convention as reading, in reverse.
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await withFormats(page, async () => {
    await page.getByLabel("From format").selectOption("xml");
    await page.getByLabel("To format").selectOption("xml");
  });
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SOAP_REQUEST);

  // The sample needs a value between the tags, not just the attribute: an element with
  // no text has no text node to make a rule for.
  await buildFromSample(page, `<order><weight unit="kg">0</weight></order>`);

  const names = page.getByRole("textbox", { name: "Output field name" });
  await expect(names).toHaveCount(2);

  const sourceOf = (n: number) => page.getByLabel("Source field", { exact: true }).nth(n);
  await sourceOf(0).fill("Envelope.Body.shipping.weight.@unit");
  await sourceOf(1).fill("Envelope.Body.shipping.weight.#text");

  await expectPreview(page, '<weight unit="kg">0.940</weight>');
});

test("an attribute can be added to an element by hand, without a sample", async ({ page }) => {
  const subscriptionId = await createSubscription(page);
  await openMapper(page, subscriptionId);
  await withFormats(page, async () => {
    await page.getByLabel("From format").selectOption("xml");
    await page.getByLabel("To format").selectOption("xml");
  });
  await page.getByRole("textbox", { name: "Sample source document" }).fill(SOAP_REQUEST);

  // Dots separate the levels, so `order.weight.@unit` puts the attribute on `weight`
  // two levels down. Nothing about attributes needs its own control.
  await addFixedRule(page, "order.weight.@unit", "kg");
  await addPathRule(page, "order.weight.#text", "Envelope.Body.shipping.weight.#text");

  await expectPreview(page, '<weight unit="kg">0.940</weight>');
});
