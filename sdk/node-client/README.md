# @simplyworks/bitween-client

Bitween's exchanges from your own program, as a service account: search them, read their files,
follow their retry chains, retry and export them. The JavaScript and TypeScript form of
`SimplyWorks.Bitween.Client`, with the same calls. Node 22 or later, no dependencies.

```js
const { BitweenClient } = require("@simplyworks/bitween-client");

const bitween = new BitweenClient({ url: "https://bitween.example.com", token: process.env.BITWEEN_TOKEN });

const failed = await bitween.exchanges.search({ status: "failed", partnerIds: [7] });
const retryId = await bitween.exchanges.retry(failed.result[0].id);
```

The token is a service account's (Team > Service accounts in Bitween). Keep it on your server, never
in browser code. See [Client SDK](../../docs/client-sdk.md) for every call, queries, selections and errors.

Tests: `node --test`. They run against a stand-in Bitween; the same calls against a real one are tested
in `SW.Bitween.HttpTests`, through the .NET client.
