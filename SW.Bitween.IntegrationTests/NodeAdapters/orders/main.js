// A Bitween handler in JavaScript (an ES module): answers an order with its settings, or rejects it as bad data.
// JavaScript rather than TypeScript so it runs on any Node CI has; SW-Serverless tests the TypeScript build.
import { expect, run, valueOf } from "@simplyworks/sw-serverless";
import { ExchangeFile, Handler } from "@simplyworks/bitween";

class Orders extends Handler {
  constructor() {
    super();
    expect("Partner", { description: "Who receives the orders" });
    expect("Token", { secret: true, description: "The partner's key" });
  }

  handle(file) {
    const order = JSON.parse(file.data);
    if (order.reject) return new ExchangeFile({ data: '{"error":"rejected"}', badData: true });
    const answer = { order, to: valueOf("Partner"), token: (valueOf("Token") ?? "").length };
    return new ExchangeFile({ data: JSON.stringify(answer), filename: "answer.json" });
  }
}

run(Orders);
