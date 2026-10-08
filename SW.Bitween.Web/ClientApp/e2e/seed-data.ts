/**
 * The rows the suite depends on, by name.
 *
 * The `seed` setup project finds or creates each of these before any spec runs, so the suite
 * works on a database created a minute ago as well as on a long-lived dev database that already
 * has them. Specs refer to these constants rather than repeating the strings, so a rename here
 * cannot leave a spec looking for a row that is no longer made.
 *
 * None of them may match the purge patterns in seed.setup.ts ("Playwright …", "PW …"): those
 * rows are deleted at the start of every run, and a seeded row caught by them would be deleted
 * and recreated each time — or, worse, deleted after something had been built on top of it.
 */
export const SEED = {
  /** A partner to attach to gateways. Has no adapters or keys of its own. */
  partner: "Acme Retail",

  /** A JSON information type: what every subscription the specs create is about. */
  informationType: "Shipment order",
  /**
   * Its code. Optional in the product, but real types carry one, and list pages show the code
   * where there is one — table-layout.spec relies on that column holding a short code.
   */
  informationTypeCode: "SHIPMENT_ORDER",

  /**
   * A bus-enabled information type that no bus gateway listens for. Creating a bus gateway
   * needs one, and a type can only have one gateway, so the seed refuses to go on if something
   * other than the suite's own leftovers has claimed it.
   */
  busInformationType: "Delivery proof",
  busInformationTypeCode: "DELIVERY_PROOF",
  /** Its bus message name. No spaces: it becomes a RabbitMQ routing key. */
  busMessageTypeName: "e2e.delivery-proof",
  /** Its one promoted property. A route's filter can only match on promoted properties. */
  busFilterProperty: { key: "Carrier", path: "$.carrier" },

  /**
   * A subscription that delivers to a port nothing listens on, so every exchange addressed to it
   * fails. The exchanges, dashboard and table-layout specs need failed exchanges to act on, and
   * this is the one honest way to get them: a real delivery that really failed.
   */
  failingSubscription: "E2E unreachable delivery",
  failingUrl: "http://127.0.0.1:9/unreachable",

  /** How many failed exchanges nobody has retried yet the specs can count on. */
  unretriedFailures: 3,
} as const;
