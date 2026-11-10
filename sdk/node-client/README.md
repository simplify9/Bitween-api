# @simplyworks/bitween-client

Bitween from your own program, as a service account: search exchanges, read their files, follow their
retry chains, retry and export them, and set integrations up: partners, subscriptions, API and bus
gateways, notification channels. `BitweenGateway` sends to an API gateway as a partner. The JavaScript
and TypeScript form of `SimplyWorks.Bitween.Client`, with the same calls. Node 22 or later, no
dependencies.

```js
const { BitweenClient } = require("@simplyworks/bitween-client");

const bitween = new BitweenClient({ url: "https://bitween.example.com", token: process.env.BITWEEN_TOKEN });

const failed = await bitween.exchanges.search({ status: "failed", partnerIds: [7] });
for (const exchange of failed.result) console.log(exchange.id, exchange.exception);

const partnerId = await bitween.partners.create({ name: "Customer 42", adapterProperties: { apiToken }, secretProperties: ["apiToken"] });
```

The token is a service account's (Team > Service accounts in Bitween). Keep it on your server, never
in browser code. See [Client SDK](https://github.com/simplify9/Bitween-api/blob/releases/r10.0/docs/client-sdk.md) for every call, queries, selections and errors.

Tests: `node --test`. They run against a stand-in Bitween; the same calls against a real one are tested
in `SW.Bitween.HttpTests`, through the .NET client.
