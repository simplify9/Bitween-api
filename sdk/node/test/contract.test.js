"use strict";
const test = require("node:test");
const assert = require("node:assert");
const fs = require("node:fs");
const path = require("node:path");

const sdk = process.env.SW_SERVERLESS_NODE ?? path.join(__dirname, "..", "..", "..", "..", "SW-Serverless", "sdk", "node", "src");
const sw = require(sdk);
const { ExchangeFile, Handler, Mapper, Receiver, ValidationResult, Validator } = require("../src");

const contractFolder = path.join(__dirname, "..", "..", "..", "SW.Bitween.Adapters", "Contract");
const contract = JSON.parse(fs.readFileSync(path.join(contractFolder, "bitween-adapter-contract.v1.json"), "utf8"));

class Echo extends Handler { handle(f) { return f; } }
class Upper extends Mapper { map(f) { return new ExchangeFile({ data: f.data.toUpperCase() }); } }
class Check extends Validator { validate(f) { return f.data ? null : { Data: "empty" }; } }
class Inbox extends Receiver { listFiles() { return []; } getFile() { return new ExchangeFile(); } deleteFile() {} }

test("each kind exposes exactly the contract's methods", () => {
  for (const cls of [Echo, Upper, Check, Inbox]) {
    const described = sw.describe(cls);
    const kind = described.kinds[0];
    assert.deepStrictEqual(described.contracts, { bitween: 1 });
    const methods = contract.kinds[kind].methods;
    assert.deepStrictEqual(described.commands.map((c) => c.name).sort(), methods.map((m) => m.name).sort(), kind);
    for (const m of methods) {
      const c = described.commands.find((x) => x.name === m.name);
      assert.strictEqual(c.returnsValue, m.output !== null, m.name);
      assert.strictEqual(c.inputSchema === null, m.input === null, m.name);
    }
  }
});

test("exchange files carry the contract's property names", () => {
  const schema = JSON.parse(fs.readFileSync(path.join(contractFolder, "exchange-file.schema.json"), "utf8"));
  const wire = new ExchangeFile({ data: "{}", filename: "a.json", badData: true, contentType: "application/json" }).toWire();
  assert.deepStrictEqual(Object.keys(wire).sort(), Object.keys(schema.properties).sort());
  assert.strictEqual(wire.Hash, "bf21a9e8fbc5a3846fb05b4fa0859e0917b2202f");
});

test("the contract's examples read and write back the same", () => {
  for (const kind of Object.values(contract.kinds))
    for (const m of kind.methods)
      for (const example of m.examples ?? []) {
        const back = ExchangeFile.fromWire(example).toWire();
        for (const [k, v] of Object.entries(example)) assert.deepStrictEqual(back[k], v);
      }
});

test("a validation result says success from its failures", () => {
  assert.deepStrictEqual(new ValidationResult().toWire(), { Success: true, Validations: [] });
  assert.deepStrictEqual(new ValidationResult().add("orderId", "missing").toWire(),
    { Success: false, Validations: [{ Key: "orderId", Value: "missing" }] });
});

test("a kind without its methods fails when it starts", () => {
  class Lazy extends Receiver { listFiles() { return []; } }
  assert.throws(() => new Lazy().__swCheck(), /getFile, deleteFile/);
  new Echo().__swCheck();
});

test("a validator's answer is read in any natural shape", async () => {
  const wire = await new Check().__swValidate({ Data: "" });
  assert.deepStrictEqual(wire.toWire(), { Success: false, Validations: [{ Key: "Data", Value: "empty" }] });
});
