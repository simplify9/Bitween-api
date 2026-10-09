# Adapters

Adapters do the work at each stage of a subscription. Bitween has four kinds.

| Kind | Stage | Contract |
|---|---|---|
| Receiver | Pulls items for a scheduled job | `IInfolinkReceiver`: `Initialize`, `ListFiles`, `GetFile(id)`, `DeleteFile(id)`, `Finalize` |
| Validator | Checks API input before an exchange is created | `IInfolinkValidator`: `Validate(file)` returns success or a list of errors |
| Mapper | Transforms the input | `IInfolinkHandler`: `Handle(file)` returns the output |
| Handler | Delivers and returns an optional response | `IInfolinkHandler`: `Handle(file)` returns the response |

The contracts come from `SimplyWorks.PrimitiveTypes`. The payload is an `XchangeFile` with text `Data`, a `Filename`, a `ContentType` and a `BadData` flag. Notifiers and retry alerts reuse handler adapters.

## Native and custom adapters

- **Native adapters** are compiled into Bitween and run in process. Their ids start with `Native`, such as `NativeHttpHandler`.
- **Custom adapters** are separate .NET console programs that `SimplyWorks.Serverless` downloads from object storage. A *classic* custom adapter runs as a new process for each call. A *resident* one runs as a long-lived process that keeps its connections open.
- **Data source adapters** are resident adapters for brokers (`bitween.bus.rabbitmq`, `bitween.bus.sqs`) and databases (`bitween.db.postgresql`, `bitween.db.mysql`, `bitween.db.sqlserver`, `bitween.db.oracle`). A subscription binds them to a data source. See [Data sources](data-sources.md), [External brokers](external-brokers.md) and [Databases](databases.md).

An id starting with `native`, ignoring case, is a native adapter. For any other id, Bitween reads the package's metadata and runs it as resident or classic. Resident adapters only run on nodes with `Bitween:BusProvidersEnabled`.

## Properties

Each adapter declares its properties. The UI shows which are required, which are secret, and each one's description and default.

- **Tokens.** Values can contain `{{partner.KEY}}` and `{{globals.SET.KEY}}`. Bitween substitutes them ignoring case, global values first, when an exchange is created. Unresolved tokens stay as written. Receivers only get global values, and notifier properties get neither. A response or bus gateway subscription's handler can also use `{{source.PATH}}`, a value from the original document; see [Response routing](exchange-pipeline.md#6-response-routing).
- **Secrets.** Secret values are never sent to the browser. The API returns `__private__` instead, and sending `__private__` back keeps the stored value. If Bitween cannot describe an adapter, it masks every property. A property is secret only if its adapter says so: `[Secure]` on a native adapter, `isPrivate: true` in `Runner.Expect` on a published one. Anything else is returned as stored. A secret property's default is never sent either, but the adapter still applies it when the value is left empty.
- **Required properties** are checked when a subscription is saved. A blank value counts as missing.
- **`xchangeid`** is added to mapper and handler properties at run time.
- A value that does not convert to the property's type, such as `BatchSize=abc`, silently falls back to the default.
- The admin UI loads every adapter of a kind, with its properties, in one call. Descriptions of custom adapters are cached on each node, so a newly uploaded version can show its old properties for a while.

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

What Bitween calls on an adapter of each kind, and what it passes, is the [adapter contract](adapter-contract.md).

A custom adapter is a .NET console application that references `SimplyWorks.Serverless.Sdk`. This is the repository's sample handler.

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
- A validator implements `IInfolinkValidator` and returns `new InfolinkValidatorResult(errors)`, where no errors means valid.
- A receiver implements `IInfolinkReceiver`.
- `Runner.CorrelationId` holds the exchange's correlation id. `AdapterLogger` forwards log lines to Bitween.

| Sample project | Shows |
|---|---|
| `SW.Bitween.SampleHandler` | A handler with one property |
| `SW.Bitween.SampleMapper` | A mapper, which is a handler |
| `SW.Bitween.SampleValidator` | A validator using FluentValidation |
| `SW.Bitween.SampleConfigurableAdapter` | A test double that can delay, fail or return fixed data |
| `SW.Bitween.SampleResidentHandler` | A handler built as a resident adapter |

### Installing a custom adapter

Custom adapter ids follow the pattern `infolink6.{kind}.{name}`, where kind is `handlers`, `receivers`, `mappers` or `validators`. The adapter picker lists packages stored under these keys.

```
{Bitween:AdapterPath}/infolink6.{kind}.{name}
{Bitween:AdapterPath}/infolink6.{kind}.{name}/{major.minor.patch}
```

A trailing semantic version segment is treated as a version of the adapter. A package is a zip of the published console application. The integration tests upload packages with `EntryAssembly` and `Hash` metadata for the serverless runner. How the runner chooses among versions is decided inside `SimplyWorks.Serverless` and is not visible in this repository.

A custom adapter may run for `Bitween:ServerlessCommandTimeout` seconds, 300 by default.

An adapter in another runtime than .NET, and a .NET version published without being made current, has no package at `{Bitween:AdapterPath}/{id}`. The picker finds it in the catalog (`{Bitween:AdapterPath}-catalog/{id}.json`) instead, under the kinds its manifest declares, while it has a version that has not been withdrawn.

### Reading an adapter's source

A package built with `serverless build` carries the adapter's source under `source/`, and its manifest lists each file with its SHA-256. On the **Adapters** page, a version that carries source has a **View** link in the Versions table. It opens the version's files; **Compare with** shows which files another version added, removed or changed, and a diff of each.

Each file is read from the package in storage and checked against the hash in its manifest. A file that does not match is not shown. Versions published before packages carried source, or with `--no-source`, have no link.

Reading source needs the `adapter-source.view` permission, which only administrators have unless a custom role grants it. Every file read is recorded in the audit trail.

To write a native adapter instead, see [Development](development.md#adding-a-native-adapter).
