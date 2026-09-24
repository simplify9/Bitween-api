import { test, expect } from "@playwright/test";
import { signInAsAdmin } from "./helpers";
import {
  createSubscription,
  expectPreview,
  openMapper,
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
