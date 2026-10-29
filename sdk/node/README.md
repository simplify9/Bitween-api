# @simplyworks/bitween

The Bitween adapter contract for JavaScript and TypeScript: the kinds of adapter Bitween runs and what
it passes them. It is the JavaScript form of `SW.Bitween.Adapters/Contract/bitween-adapter-contract.v1.json`,
as `SimplyWorks.Bitween.Adapters` is its .NET one and `simplyworks-bitween` its Python one. It needs
[`@simplyworks/sw-serverless`](https://github.com/simplify9/SW-Serverless/tree/main/sdk/node), Node 22 or later.

| Kind | Implement | Bitween calls |
|---|---|---|
| `Handler` | `handle(file)` returning an `ExchangeFile` | `Handle` |
| `Mapper` | `map(file)` returning an `ExchangeFile` | `Handle` |
| `Validator` | `validate(file)` returning a `ValidationResult` (or `[key, message]` pairs, or `{ key: message }`) | `Validate` |
| `Receiver` | `listFiles()`, `getFile(fileId)`, `deleteFile(fileId)`; optionally `initialize()`, `finalize()` | `Initialize`, `ListFiles`, `GetFile`, `DeleteFile`, `Finalize` |

```ts
import { expect, run } from "@simplyworks/sw-serverless";
import { ExchangeFile, Handler } from "@simplyworks/bitween";

class Orders extends Handler {
  constructor() {
    super();
    expect("Url", { description: "Where orders go" });
  }

  handle(file: ExchangeFile): ExchangeFile {
    // A partner's rejection is returned with badData: true, not thrown.
    return new ExchangeFile({ data: file.data, filename: file.filename });
  }
}

run(Orders);
```

`ExchangeFile` has `data`, `filename`, `badData` and `contentType`, and is sent with the contract's
property names (`Data`, `Filename`, `BadData`, `ContentType`, `Hash`). A kind missing a method it needs
fails when the adapter starts.

`bitween adapter init --lang typescript` (or `--lang node`) starts an adapter, and `bitween adapter build`
vendors this package and the SDK into it from this folder, so nothing needs installing from npm.

Tests: `node --test` (with SW-Serverless beside this repository, or `SW_SERVERLESS_NODE` pointing at its `sdk/node/src`).
