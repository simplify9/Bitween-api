# The Bitween adapter contract

What Bitween calls on an adapter and what it passes, whatever language the adapter is written in. The machine-readable form is `SW.Bitween.Adapters/Contract/bitween-adapter-contract.v1.json`, with a JSON Schema for each payload beside it. The SDK for each language is built from those files, and so is the conformance check that `serverless test` runs.

This is version 1. Later versions only add things, so an adapter written for version 1 keeps working.

## How Bitween calls an adapter

Bitween calls an adapter's methods **by name**, over the SW-Serverless protocol. The names below are exact and case-sensitive. Payloads are encoded like this:

- A string argument or result is the **raw UTF-8 text**, not a JSON string.
- Any other argument or result is **JSON**, with property names exactly as its schema gives them (`Data`, not `data`).
- A method with no argument receives an empty payload, and one with no result returns an empty payload.

## Kinds

| Kind | Methods | Takes | Returns |
|---|---|---|---|
| `handler`: delivers a message | `Handle` | ExchangeFile | ExchangeFile, the partner's response |
| `mapper`: transforms a message | `Handle` | ExchangeFile | ExchangeFile, mapped |
| `validator`: checks a message before it is accepted | `Validate` | ExchangeFile | ValidationResult |
| `receiver`: fetches files on a schedule | `Initialize` | — | — |
| | `ListFiles` | — | a JSON array of file ids |
| | `GetFile` | a file id | ExchangeFile |
| | `DeleteFile` | a file id | — |
| | `Finalize` | — | — |

A receiver gets one session per run, with calls in this order:
1. `Initialize`
2. `ListFiles`
3. for each file, `GetFile`, then `DeleteFile` once Bitween has safely taken the file in
4. `Finalize`, which is also called after a failure

A file id is whatever `ListFiles` returned, passed back exactly.

The bus and database provider kinds that Bitween's built-in providers implement are .NET-only for now.

## Payloads

**ExchangeFile** ([schema](../SW.Bitween.Adapters/Contract/exchange-file.schema.json))

| Property | Type | |
|---|---|---|
| `Data` | string, required | The content: text, or base64 for binary content. |
| `Filename` | string or null | The file's name, when it has one. |
| `BadData` | boolean | A bad response: a delivery the partner rejected. |
| `ContentType` | string or null | The content's media type, when known. |
| `Hash` | string | SHA-1 of `Data`. .NET adapters write it. Bitween always recomputes it and never trusts the value sent. |

Unknown properties are allowed and ignored.

**ValidationResult** ([schema](../SW.Bitween.Adapters/Contract/validation-result.schema.json))

| Property | Type | |
|---|---|---|
| `Validations` | array of `{"Key": string, "Value": string}`, required | One entry per failure: a code (often the field it concerns) and a message. Empty means it passed. |
| `Success` | boolean | True when `Validations` is empty. Bitween derives it from `Validations`. |

## Errors and rejections

A call fails when the adapter raises an error. Bitween records the error's type and message on the exchange, and the subscription's retry policy decides what happens next.

A handler whose partner **rejected** the message doesn't fail. It returns the partner's answer with `BadData` set to true. That's how Bitween tells "the partner said no", which a retry won't fix, from "we couldn't reach the partner", which it might.

## In each language

| Language | Package | Kinds |
|---|---|---|
| .NET | `SimplyWorks.Bitween.Adapters` | `IBitweenHandler`, `IBitweenMapper`, `IBitweenValidator`, `IBitweenReceiver`, with `ExchangeFile` and `ValidationResult` |
| Python | `simplyworks-bitween` | to come |
| Node.js / TypeScript | `@simplyworks/bitween` | to come |
| Go | Bitween-api `sdk/go` module | to come |

.NET adapters built on `SW.PrimitiveTypes` (`IInfolinkHandler`, `IInfolinkValidator`, `IInfolinkReceiver`, `XchangeFile`) keep working unchanged. They use the same method names and produce the same JSON, which the tests in `SW.Bitween.UnitTests/AdapterContractTests.cs` check.
