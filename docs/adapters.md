# Adapters

Adapters do the work at each stage of a subscription. Bitween has four kinds.

| Kind | Stage | Methods |
|---|---|---|
| Receiver | Pulls items for a scheduled job | `Initialize`, `ListFiles`, `GetFile(id)`, `DeleteFile(id)`, `Finalize` |
| Validator | Checks API input before an exchange is created | `Validate(file)` returns success or a list of errors |
| Mapper | Transforms the input | `Handle(file)` returns the output |
| Handler | Delivers and returns an optional response | `Handle(file)` returns the response |

What Bitween calls and passes is the [adapter contract](adapter-contract.md). In .NET its interfaces are `IBitweenReceiver`, `IBitweenValidator`, `IBitweenMapper` and `IBitweenHandler` from `SimplyWorks.Bitween.Adapters`. Adapters written against the older `IInfolinkReceiver`, `IInfolinkValidator` and `IInfolinkHandler` from `SimplyWorks.PrimitiveTypes`, with `XchangeFile`, are called the same way. The payload has text `Data`, a `Filename`, a `ContentType` and a `BadData` flag. Notifiers and retry alerts reuse handler adapters.

## Native and custom adapters

- **Native adapters** are compiled into Bitween and run in process. Their ids start with `Native`, such as `NativeHttpHandler`.
- **Custom adapters** are separate programs, written in .NET, Python, JavaScript or TypeScript, that `SimplyWorks.Serverless` downloads from object storage and runs. A *classic* custom adapter runs as a new process for each call. A *resident* one runs as a long-lived process that keeps its connections open.
- **Data source adapters** are resident adapters for brokers (`bitween.bus.rabbitmq`, `bitween.bus.sqs`) and databases (`bitween.db.postgresql`, `bitween.db.mysql`, `bitween.db.sqlserver`, `bitween.db.oracle`). A subscription binds them to a data source. See [Data sources](data-sources.md), [External brokers](external-brokers.md) and [Databases](databases.md).

An id starting with `native`, ignoring case, is a native adapter. For any other id, Bitween reads the package's metadata and runs it as resident or classic. Resident adapters only run on nodes with `Bitween:BusProvidersEnabled`. Classic adapters run on every node, including Python and Node ones.

## Properties

Each adapter declares its properties. The UI shows which are required, which are secret, and each one's description and default.

- **Tokens.** Values can contain `{{partner.KEY}}` and `{{globals.SET.KEY}}`. Bitween substitutes them ignoring case, global values first, when an exchange is created. Unresolved tokens stay as written. Receivers only get global values, and notifier properties get neither. A response or bus gateway subscription's handler can also use `{{source.PATH}}`, a value from the original document; see [Response routing](exchange-pipeline.md#6-response-routing).
- **Secrets.** Secret values are never sent to the browser. The API returns `__private__` instead, and sending `__private__` back keeps the stored value. If Bitween cannot describe an adapter, it masks every property. A property is secret only if its adapter says so: `[Secure]` on a native adapter, and on a published one `isPrivate: true` in `Runner.Expect` (.NET), `secret=True` in `sw.expect` (Python) or `secret: true` in `expect` (JavaScript and TypeScript), which `bitween adapter build` writes into the manifest. Anything else is returned as stored. A secret property's default is never sent either, but the adapter still applies it when the value is left empty.
- **Required properties** are checked when a subscription is saved. A blank value counts as missing.
- **`xchangeid`** is added to mapper and handler properties at run time.
- A value that does not convert to the property's type, such as `BatchSize=abc`, silently falls back to the default.
- The admin UI loads every adapter of a kind, with its properties, in one call. A published adapter is described from its manifest when the manifest lists its properties. Otherwise a .NET adapter is started and asked; an adapter in another runtime never is, and a manifest that lists none means it has none.
- Descriptions of custom adapters are cached on each node. Publishing, promoting or withdrawing a version through Bitween tells every node to forget the adapter, over the bus. A version published straight to storage shows its old properties for up to a minute.

## Rebex license

The Rebex FTP/SFTP adapters and the Rebex POP3 receiver use the commercial Rebex library. They are hidden from the adapter pickers until a key is saved in the **Rebex license key** setting, which takes effect without a restart. Each has open-source counterparts that need no license: the SFTP adapters and the FTP / FTPS adapters below, and `NativePop3Receiver`.

## Handlers

### NativeHttpHandler

Sends the payload to an HTTP endpoint.

| Property | Default | Notes |
|---|---|---|
| `Url` *(required)* | | When the payload is present and the URL contains `{{`, it is rendered as a Liquid template over the payload, so `https://api.example.com/orders/{{ order.id }}` works. |
| `Verb` | `post` | `get`, `put` or `delete`. Any other value, including `patch`, sends a POST. GET sends no body. |
| `AuthType` | | `ApiKey`, `Bearer`, `Basic`, `Login` or `OAuth2`, matched case-sensitively. Empty means no authentication. |
| `ApiKey` *(secret)* | | Used by `ApiKey`. |
| `LoginUsername` | | Used by `Basic` and `Login`. |
| `LoginPassword` *(secret)* | | Used by `Basic` and `Login`, and as the token for `Bearer`. |
| `LoginUrl` | | Token endpoint for `Login` and `OAuth2`. |
| `ClientId`, `ClientSecret` *(secret)* | | Used by `OAuth2`. |
| `ContentType` | `application/json` | `application/x-www-form-urlencoded` form-encodes a flat JSON object. `multipart/form-data` sends the payload as one part named `file`. Other types send the payload as text. |
| `Headers` *(secret)* | | Extra headers as `Name:Value` pairs separated by commas. A value cannot contain a colon. |
| `CorrelationId` | | Sent as the `request-context-correlation-id` request header. |
| `DefaultRequest` | | Body to send when the payload is empty. |

| `AuthType` | Behaviour |
|---|---|
| `ApiKey` | Adds the header `ApiKey: {ApiKey}` |
| `Bearer` | Adds `Authorization: Bearer {LoginPassword}` |
| `Basic` | Basic authentication with `LoginUsername` and `LoginPassword` |
| `Login` | POSTs `Email` and `Password` as JSON to `LoginUrl`, reads `Jwt` from the reply, and sends it as a bearer token |
| `OAuth2` | Requests a client credentials token from `LoginUrl`, reads `access_token`, and sends it as a bearer token |

| Reply status | Result |
|---|---|
| 2xx or 3xx | Success. The body becomes the response file. |
| 4xx | Bad response. The body is kept and flagged, and retry policies see it as a `BadResult`. |
| 5xx, or below 200 | The exchange fails with the status and body, so retry policies see it as an `Error`. |

### NativeSmtpHandler

Sends an email built from the payload.

| Property | Default | Notes |
|---|---|---|
| `Host` *(required)* | | |
| `Port` | `587` | Port 465 uses implicit TLS. Other ports use STARTTLS when `UseTls` is on. |
| `UseTls` | `true` | TLS is required when on, never opportunistic. |
| `Username` | `From` | |
| `Password` *(secret)* | | Authenticates only when set, and only over a secure connection. |
| `From` *(required)*, `FromName` | | |
| `To` *(required)*, `Cc`, `Bcc` | | Comma-separated addresses. |
| `Subject` *(required)*, `Body` *(required)* | | Scriban templates over the payload, with the same syntax as the legacy JSON mapper. They are rendered only when the payload is JSON, otherwise sent as written. |
| `IsHtml` | `true` | |

Server certificates must be valid. A chain whose only problem is that revocation could not be checked is accepted. The response file holds the rendered subject.

### NativeS3UploadHandler

Writes the payload to an S3-compatible bucket.

| Property | Default | Notes |
|---|---|---|
| `AccessKeyId`, `SecretAccessKey` *(secret)*, `ServiceUrl`, `BucketName` | | All required. |
| `FolderName` | | Ignored when `FileName` is set. |
| `FileName` | | Full object key. When empty, the key is `{FolderName}/{yyyyMMddHHmmss}_{guid}.{FileExtension}`. |
| `FileExtension` | | |
| `ContentType` | `text/plain` | |

The response file holds the object key.

### NativeAzureBlobUploadHandler

Writes the payload to an Azure Blob container.

| Property | Notes |
|---|---|
| `ConnectionString` *(required, secret)*, `ContainerName` *(required)* | |
| `FileName` | Blob name. When empty, a timestamp and GUID name is generated. |
| `FileExtension` | |

Existing blobs are overwritten. The response file holds the blob name.

### NativeSftpUploadHandler and NativeFtpUploadHandler

Upload the payload as a file, with open-source clients and no license: `NativeSftpUploadHandler` over SFTP (SSH.NET), `NativeFtpUploadHandler` over FTP or FTPS (FluentFTP). The file is named after the exchange file, or a UTC timestamp when it has no name, and a name from the message can't climb out of `TargetPath`.

| Property | Default | Notes |
|---|---|---|
| `Host` *(required)*, `Username` *(required)* | | |
| `TargetPath` | | Remote directory. |
| `FileNamePrefix` | | Prepended as `{prefix}_`. |
| `DataEncoding` | `utf8` | `base64` decodes the payload into bytes before uploading. |

SFTP connection properties, shared with `NativeSftpReceiver`:

| Property | Default | Notes |
|---|---|---|
| `Port` | 22 | |
| `Password` *(secret)* | | Signs in, or is the private key's passphrase when a key is set. |
| `PrivateKey` *(secret)* | | Signs in with this key instead, PEM or OpenSSH format. A PEM key pasted onto one line is re-wrapped. |
| `HostKeyFingerprint` | | The server's `SHA256:...` host-key fingerprint. When set, any other key is refused before credentials are sent. |

FTP connection properties, shared with `NativeFtpReceiver`:

| Property | Default | Notes |
|---|---|---|
| `Port` | 21, or 990 for implicit FTPS | |
| `Password` *(required, secret)* | | |
| `Encryption` | `none` | `none` for plain FTP, `explicit` for FTPS with AUTH TLS, `implicit` for FTPS that is TLS from the start. |
| `CertificateThumbprint` | | The server certificate's SHA-256 thumbprint, in hex (colons and case don't matter). When set, exactly that certificate is trusted, a self-signed one included, and any other is refused before credentials are sent. When empty, the certificate must be valid. |
| `PassiveMode` | `true` | Turn off for active mode. |

### NativeRebexFtpUploadHandler

Uploads over SFTP or plain FTP with Rebex, shown as "(Rebex)". Needs a Rebex license. Its `Protocol` property picks the protocol, where the open-source adapters are split by protocol instead: `sftp` and `sftpssh` map to `NativeSftpUploadHandler`, `ftp` to `NativeFtpUploadHandler` with `Encryption` `none`. Every other property has the same name.

| Property | Default | Notes |
|---|---|---|
| `Host` *(required)*, `Username` *(required)* | | |
| `Port` | 22 for SFTP, 21 for FTP | |
| `Protocol` | `sftp` | `sftp` or `ftp` with a password, or `sftpssh` with a private key. |
| `Password` *(secret)* | | Required for `sftp` and `ftp`. The key passphrase for `sftpssh`. |
| `PrivateKey` *(secret)* | | Required for `sftpssh`. A PEM key pasted onto one line is re-wrapped. |
| `TargetPath` | | Remote directory. |
| `FileNamePrefix` | | Prepended as `{prefix}_`. |
| `DataEncoding` | `utf8` | `base64` decodes the payload into bytes before uploading. |

The file is named after the exchange file, or a UTC timestamp when it has no name.

## Receivers

`BatchSize` caps the items taken per run and defaults to 50. `ResponseEncoding` is `utf8` or `base64`; use `base64` for binary content.

### NativeHttpReceiver

Calls an HTTP endpoint once per run and turns the JSON reply into items. Its properties match the HTTP handler's, with these differences: `Verb` defaults to `get`, there is no URL templating or multipart, `Login` posts `UserName` rather than `Email`, and there is one extra property.

| Property | Notes |
|---|---|
| `ArrayPath` | JSON path to the array of items. Without it, a root array is split into items and any other reply is a single item. |

The reply must be JSON, and a status of 400 or above fails the run. There is no pagination, items have no file name, and nothing is removed at the source.

### NativeS3Receiver

| Property | Notes |
|---|---|
| `AccessKeyId`, `SecretAccessKey` *(secret)*, `ServiceUrl`, `BucketName` | Required. |
| `FolderName` | Key prefix. No `/` is added, so `incoming` also matches `incoming-archive/`. |
| `BatchSize`, `ResponseEncoding` | |
| `DeleteMovesFileTo` | When set, each processed object is copied under this prefix and then deleted. Otherwise it is deleted. |

### NativeAzureBlobReceiver

| Property | Notes |
|---|---|
| `ConnectionString` *(required, secret)*, `ContainerName` *(required)* | |
| `FolderName` | Prefix, with a `/` added. |
| `BatchSize`, `ResponseEncoding`, `DeleteMovesFileTo` | As for S3. A blob that is already gone is skipped. |

### NativeSftpReceiver, NativeFtpReceiver and NativeRebexFtpReceiver

Read files over SFTP (`NativeSftpReceiver`), FTP or FTPS (`NativeFtpReceiver`), with open-source clients and no license, or over SFTP or plain FTP with Rebex (`NativeRebexFtpReceiver`, shown as "(Rebex)", needs a license). The connection properties are the upload handlers' for the same protocol.

| Property | Default | Notes |
|---|---|---|
| `TargetPath` | | Directory to read. |
| `BatchSize`, `ResponseEncoding` | | |
| `DeleteMovesFileTo` | | When set, processed files are moved into this directory. Otherwise they are deleted. |
| `CheckFileExistence` | `true` | Skip the delete quietly when the file is already gone. |
| `MinimumFileAgeSeconds` | 0 | A file changed more recently than this is left for the next run, so one still being uploaded isn't taken half-written. |

### NativePop3Receiver and NativeRebexPop3Receiver

Read email from a POP3 mailbox over implicit TLS on port 995. `NativePop3Receiver` uses MailKit. `NativeRebexPop3Receiver` uses Rebex and needs a license.

| Property | Notes |
|---|---|
| `Host`, `Username`, `Password` *(secret)* | Required. |
| `BatchSize`, `ResponseEncoding` | |

Each email becomes one item, named after its subject. When an email has attachments, only the first attachment is used. Otherwise its text body is used. Processed emails are deleted when the session closes.

## Mappers

| Id | Description |
|---|---|
| `NativeMapper` | Rules-based mapper for JSON and XML, with a visual editor |
| `NativeJSONMapper` | Legacy Scriban template mapper for JSON, offered only while a subscription still uses it |

See [Mapping](mapping.md). Bitween has no native validators.

## Custom adapters

What Bitween calls on an adapter of each kind, and what it passes, is the [adapter contract](adapter-contract.md). A custom adapter can be written in .NET, Python, JavaScript or TypeScript. The [bitween CLI](cli.md) writes a new one of each kind, builds it into a package, checks it against the contract and publishes it.

### Custom adapters in .NET

A .NET adapter is a console application that references `SimplyWorks.Serverless.Sdk`, and `SimplyWorks.Bitween.Adapters` for the contract's interfaces. `bitween adapter init AcmeOrders --kind handler` (`--lang dotnet` is the default) writes one targeting .NET 10, declared with `[AdapterKind("handler")]` and `[AdapterContract("bitween", 1)]` and implementing `IBitweenHandler`.

Adapters written before `SimplyWorks.Bitween.Adapters` existed use the `SimplyWorks.PrimitiveTypes` interfaces and keep working. This is the repository's sample handler, written that way.

```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;

class Program
{
    static async Task Main(string[] args) => await Runner.Run(new Handler());
}

class Handler : IInfolinkHandler
{
    public Handler()
    {
        // Declares a property named ContentType with a default value.
        Runner.Expect("ContentType", "text/plain");
    }

    public Task<XchangeFile> Handle(XchangeFile xchangeFile)
    {
        var contentType = Runner.StartupValueOf("ContentType");
        return Task.FromResult(xchangeFile);
    }
}
```

- Declare properties in the constructor with `Runner.Expect`. Overloads take a default value, whether the property is private, and a description.
- Read values with `Runner.StartupValueOf(name)`.
- A validator implements `IBitweenValidator` and returns a `ValidationResult`, adding each failure with `AddError(code, message)`; no errors means valid. With `SimplyWorks.PrimitiveTypes` it implements `IInfolinkValidator` and returns `new InfolinkValidatorResult(errors)`.
- A receiver implements `IBitweenReceiver`, or `IInfolinkReceiver`.
- `Runner.CorrelationId` holds the exchange's correlation id. `AdapterLogger` forwards log lines to Bitween.

| Sample project | Shows |
|---|---|
| `SW.Bitween.SampleHandler` | A handler with one property |
| `SW.Bitween.SampleMapper` | A mapper, which is a handler |
| `SW.Bitween.SampleValidator` | A validator using FluentValidation |
| `SW.Bitween.SampleConfigurableAdapter` | A test double that can delay, fail or return fixed data |
| `SW.Bitween.SampleResidentHandler` | A handler built as a resident adapter |

### Custom adapters in Python

A custom adapter can also be written in Python 3.12 or later, with `sw-serverless` and the
Bitween kinds in `simplyworks-bitween` (this repository's `sdk/python`). Subclass a kind and
implement its methods; settings are declared with `sw.expect` as `Runner.Expect` declares them in
.NET.

```python
import sw_serverless as sw
from simplyworks_bitween import ExchangeFile, Handler


class Orders(Handler):
    def __init__(self):
        sw.expect("Url", description="Where orders go")
        sw.expect("ApiKey", secret=True)

    def handle(self, file: ExchangeFile) -> ExchangeFile:
        # A partner's rejection is returned with bad_data=True, not raised.
        return ExchangeFile(data=file.data, filename=file.filename)


if __name__ == "__main__":
    sw.run(Orders)
```

| Kind | Implement |
|---|---|
| `Handler` | `handle(file)`, returning an `ExchangeFile` |
| `Mapper` | `map(file)`, returning an `ExchangeFile` |
| `Validator` | `validate(file)`, returning a `ValidationResult` |
| `Receiver` | `list_files()`, `get_file(file_id)`, `delete_file(file_id)`, and optionally `initialize()` and `finalize()` |

The [bitween CLI](cli.md) does the rest:

```sh
bitween adapter init AcmeOrders --lang python --kind handler
cd AcmeOrders
bitween adapter build                          # the package, with the SDKs and requirements.txt vendored
bitween adapter test --settings settings.json  # the Bitween contract checks
bitween login https://bitween.example.com      # once
bitween adapter publish bin/serverless/acme.orders-0.1.0.zip
```

A Python adapter is published to its versions and the catalog only, never to
`{Bitween:AdapterPath}/{id}`, so a host older than SW-Serverless 10.1 never sees it. An id that already
has a .NET package there can't be reused for a Python or Node adapter. Bitween runs it with `python3`,
which the Docker image includes (Ubuntu 24.04's Python 3.12). Requirements are vendored into the package
with pip; one with native code is vendored for `linux-x64` and `linux-arm64` unless `adapter.json` lists
other `platforms`.

Python and Node adapters connect to Bitween over a Unix domain socket, so they run only on Linux and
macOS hosts. They run through SW-Serverless's resident adapter host, which Bitween starts on every
node, whether or not `Bitween:BusProvidersEnabled` is on.

### Custom adapters in JavaScript or TypeScript

The same, on Node 22 or later, with `@simplyworks/sw-serverless` and `@simplyworks/bitween`
(`sdk/node`). Extend a kind and implement its methods, named as in Python but in camelCase:
`handle`, `map`, `validate`, and `listFiles`, `getFile`, `deleteFile`.

```ts
import { expect, run, valueOf } from "@simplyworks/sw-serverless";
import { ExchangeFile, Handler } from "@simplyworks/bitween";

class Orders extends Handler {
  constructor() {
    super();
    expect("Url", { description: "Where orders go" });
  }

  handle(file: ExchangeFile): ExchangeFile {
    return new ExchangeFile({ data: file.data, filename: file.filename });
  }
}

run(Orders);
```

`bitween adapter init AcmeOrders --lang typescript --kind handler` (or `--lang node` for JavaScript) starts
one. `bitween adapter build` needs no TypeScript compiler: Node strips the types, so only syntax that
strips cleanly is allowed — no enums, namespaces or parameter properties. Dependencies in `package.json`
are installed into the package with npm; one with native code has to be built on the platform the adapter
runs on. Bitween runs it with `node`, which the Docker image includes (the Node 22 binary, without npm).
It runs on the same hosts and nodes as a Python adapter.

### Publishing a custom adapter

Build the adapter with `bitween adapter build`, then publish the package:

```sh
bitween login https://bitween.example.com
bitween adapter publish bin/serverless/acme.orders-1.0.0.zip            # a new version, not current yet
bitween adapter promote acme.orders 1.0.0                               # make it the one that runs
bitween adapter versions acme.orders
```

Publishing through Bitween needs `adapter-source.operate`, so an author needs a Bitween account rather
than the storage's keys, and every version is in the audit trail. Bitween checks the package's manifest
and settles its version; it does not run the contract checks, so run `bitween adapter test` first. With
`-p` and the storage flags, the CLI publishes straight to storage instead, as `sw-serverless publish`
does — for CI that holds the keys.

What is published, and where:

```
{Bitween:AdapterPath}/{id}                       the current package, for hosts older than versions (.NET only)
{Bitween:AdapterPath}-versions/{id}/{version}    every version
{Bitween:AdapterPath}-catalog/{id}.json          versions, the current one, and each one's manifest
```

A version is published without being made current unless `--current` is given, so subscriptions keep
running what they ran until it is promoted, or until a subscription pins it. Making an older version
current again is how a release is rolled back. A withdrawn version stays listed but can't be pinned
or made current; subscriptions already pinned to it keep running it.

On the **Adapters** page, someone with `adapter-source.operate` can do the same from the browser:
**Upload package** publishes a `.zip` built by `bitween adapter build` (any language, with the version
taken from the package or bumped, and made current only when ticked), and each version that isn't
current has **Make current** and **Withdraw**.

The adapter's kinds come from its manifest — `bitween adapter build` writes them from what the code
declares — so an adapter can be named anything. Packages published before manifests are found by the
older convention, `infolink6.{kind}s.{name}`, and by the `Kind` metadata they were published with.

An adapter in another runtime than .NET, and a .NET version published without being made current,
has no package at `{Bitween:AdapterPath}/{id}`. The picker finds it in the catalog instead, under the
kinds its manifest declares, while it has a version that has not been withdrawn.

A manifest can name the oldest Bitween the adapter works with, in `compatibility.applications.bitween`
(manifests written before SimplyWorks.Serverless 10.2 used `compatibility.minBitweenVersion`, which is
still read). Saving a subscription that changes a stage to such an adapter, or pins such a version, on
an older Bitween is refused with `ADAPTER_NEEDS_NEWER_BITWEEN`.

A custom adapter may run for `Bitween:ServerlessCommandTimeout` seconds, 300 by default.

The **Adapters** page names what each custom adapter runs on (.NET, Python, Node.js or a native
binary), from its current version's manifest, with a Runtime column in its Versions table and a runtime
filter once more than one is in use. Beside a subscription's adapter, the runtime of the version it
runs is named, and the version picker names each version's runtime when they differ, since pinning
one can move a step to another runtime.

### Writing an adapter in Bitween

Python, JavaScript and TypeScript adapters can be written in Bitween itself, without the CLI.

- **New adapter** on the Adapters page starts one from the template `bitween adapter init` writes. **Edit** on a
  published Python or Node version starts a draft from the source that version carries.
- A draft is saved in Bitween's database. The editor shows its files; **Save and check** builds it on the
  server and runs the Bitween contract checks `bitween adapter test` runs. **Try** calls a command with the
  settings and input given. Settings typed in the editor are sent with each call and never stored.
- **Publish** builds and checks it again and publishes a new version. A version published from the editor
  is not current: subscriptions keep running what they ran until **Make current** is used, on the
  editor's Publish tab or on any version in the Versions table, or until a subscription pins it.
  Making an older version current is how a release is rolled back.

By default the editor builds adapters that need only the SDK; one whose `requirements.txt` or
`package.json` names other packages is refused, with the CLI as the way to build it. With
`Bitween:AdapterEditorDependencies` on, the server fetches them from PyPI and npm, or from the mirrors
`PIP_INDEX_URL` and `NPM_CONFIG_REGISTRY` name. Only plain names and versions are accepted: no URLs,
paths, git sources or pip options, wheels only for Python, and no install scripts for Node, so nothing a
package brings runs while it is built. A `package-lock.json` in the draft is ignored, since it can name
any source.

Building, checking and trying run the draft's code on the Bitween server, at most two at a time. Each
call may take 30 seconds, and a run may use 256 MB and keep one core busy
(`Bitween:AdapterEditorMemoryMb`, `Bitween:AdapterEditorCpuCores`); past either it is stopped and the
editor says which limit it hit. A draft holds at most 200 files and 2 MB. Python and Node versions that
carry their source can be opened in the editor; .NET versions are built with the CLI.

Two members can have a draft open at once. A save made after someone else saved is refused, naming who
and when, and the editor offers to load their version or to save over it.

| Permission | Allows |
|---|---|
| `adapter-source.view` | Reading a published version's source and comparing versions |
| `adapter-source.edit` | Drafts: starting, editing, deleting, checking and trying them |
| `adapter-source.operate` | Publishing a version from a draft or as a package through the API (`bitween adapter publish`), making any published version current, and withdrawing one |

All three are Administration permissions: administrators have them, and a custom role can be granted
them. Drafts are in the audit trail, each save recorded by a hash of its files, and so is every version
published, made current or withdrawn through Bitween (`AdapterRelease`).

### Reading an adapter's source

A package built with `bitween adapter build` (or SW-Serverless's `sw-serverless build`) carries the adapter's source under `source/`, and its manifest lists each file with its SHA-256. On the **Adapters** page, a version that carries source has a **View** link in the Versions table. It opens the version's files; **Compare with** shows which files another version added, removed or changed, and a diff of each.

Each file is read from the package in storage and checked against the hash in its manifest. A file that does not match is not shown. A file larger than 1 MB, or that isn't UTF-8 text, is listed but its content is not shown. Versions published before packages carried source, or with `--no-source`, have no link.

Reading source needs the `adapter-source.view` permission, which only administrators have unless a custom role grants it. Every file read is recorded in the audit trail.

To write a native adapter instead, see [Development](development.md#adding-a-native-adapter).
