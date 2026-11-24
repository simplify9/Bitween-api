# Storage

How Bitween uses object storage: what it stores where, which storage calls each feature makes, and
what it depends on from the storage library. To configure storage, see
[Configuration](configuration.md#storage) and [Deployment](deployment.md#object-storage). For the
exchange-file key and served links, see [Architecture](architecture.md#storage).

Bitween reaches storage only through the `ICloudFilesService` and `ICloudFilesLifecycle` interfaces
from `SimplyWorks.PrimitiveTypes`, implemented by the
[SimplyWorks.CloudFiles](https://github.com/simplify9/SW-CloudFiles) providers. The same calls behave
differently on each provider in places (metadata keys, missing keys, URLs). The library's
[provider behaviour reference](https://github.com/simplify9/SW-CloudFiles/blob/main/docs/provider-behavior.md)
is the authority on those differences; this page notes where Bitween depends on them.

## Provider

`Bitween:StorageProvider` chooses the provider at startup (`Startup.cs`):

| Value | Provider | Notes |
|---|---|---|
| `S3` (default, also any unknown value) | `AddS3CloudFiles` | Amazon S3 and S3-compatible stores |
| `AS` | `AddAsCloudFiles` | also starts `PrivateAzureContainer` |
| `OC` | `AddOracleCloudFiles` | |
| `Local` | `AddLocalTestsCloudFiles` | refused unless `ASPNETCORE_ENVIRONMENT` is `Development` |

A failure while the provider is registered (unreachable store, wrong bucket, missing permission)
becomes a startup problem naming `CloudFiles:BucketName`. The S3 provider checks or creates the
bucket and its lifecycle rules during registration, so a key without those permissions stops
startup unless the library's `DisableAutoLifecycle` is set.

## What is stored where

| Key | Written by | Contents | Visibility | Removed by |
|---|---|---|---|---|
| `{prefix}/{exchangeId}/{input\|output\|response}` | `XchangeService` | exchange payloads, as text | provider default (private) | the bucket's lifecycle rule for the prefix |
| `{DocumentPrefix}/.access-check` | `StorageCheck`, `StorageAccess` | probe file | provider default | overwritten each start |
| `archive/{DocumentPrefix without tempN/}/{subscription}/yyyy/MM/dd/{value}_{id}.json` | `ExchangeArchive` | archived exchange, JSON | provider default | never |
| `background-requests/{id}` | `BackgroundRequests` | result of a background request (files, or bodies over 1 MB) | provider default | `BackgroundRequests` once expired |
| `mirror/standins.json`, `mirror/tests/...` | `MirrorService` | mirror stand-ins, tests, verdicts, sample files | provider default | never |
| `{AdapterPath}/{id}`, `{AdapterPath}-versions/{id}/{version}`, `{AdapterPath}-catalog/` | adapter tooling (`SimplyWorks.Serverless`), `MarketplaceInstaller` | adapter packages, versions, catalog entries | provider default | never (withdrawing a version only marks it in the catalog) |
| `{AdapterPath}/infolink6.{kind}...` | older adapter packages | adapters published before the catalog | provider default | by hand |

`{prefix}` is the exchange's recorded `FilesPrefix`, else `Bitween.LegacyDocumentPrefix`, else
`Bitween:DocumentPrefix` (`XchangeService.FileKey`). `AdapterPath` defaults to `adapters` and
`DocumentPrefix` to `temp30/Bitweendocs`. The Helm chart sets its own `documentPrefix` default,
`temp30/infolinkdocs`.

No write sets `WriteFileSettings.Public`, so every object takes the provider's default, which is
private everywhere except an Azure container left public (see [Privacy](#privacy)).

## Calls by feature

| Feature | Write | Read | List | Other |
|---|---|---|---|---|
| Exchange files (`XchangeService`) | `WriteTextAsync` | `OpenReadAsync`, read whole into a string | | `GetUrl` for the URL base only |
| Served file links (`FileLinksController`) | | `OpenReadAsync`, streamed as `text/plain` | | |
| Background requests | `WriteAsync` | `OpenReadAsync`, streamed | | `DeleteAsync` (result ignored) |
| Archive (`ExchangeArchive`) | `WriteTextAsync` | `OpenReadAsync`, whole file, about 32 MB in flight at most (8 reads at once) | | |
| Retention (`StorageRetention`) | | | | `ICloudFilesLifecycle.GetLifecycleAsync`, cached 5 min (1 min after a failure) |
| Startup check (`StorageCheck`) | `WriteTextAsync` | `OpenReadAsync` | | 30 s per step |
| Bucket openness (`StorageAccess`) | `WriteTextAsync` | anonymous HTTP GET of `GetUrl` | | cached 5 min |
| Adapter search and listing | | | prefix `{AdapterPath}/infolink6.{kind}`, and all of `{AdapterPath}/` | `GetMetadataAsync` per adapter, 16 at once |
| Adapter catalog and source | | `OpenReadAsync` to a temp file | exact key, as an existence check | |
| Data source provider catalog | | | all of `{AdapterPath}/` | |
| Marketplace | | | catalog root; `{AdapterPath}/{id}` as an existence check | |
| Mirror | `WriteTextAsync`, `WriteAsync` | `OpenReadAsync`, whole file | | |
| Native S3 receiver | | `OpenReadAsync`, whole file | `{FolderName}`, then `BatchSize` | `DeleteAsync`; S3 `CopyObject` for `DeleteMovesFileTo` |
| Native S3 upload handler | `WriteAsync` | | | |

`GetSignedUrl` and `OpenWrite` are never called. Bitween serves files itself through sealed links
instead (see [Architecture](architecture.md#storage)).

## What Bitween depends on

### Missing files

Each provider throws its own exception for a missing key. `StorageErrors.IsNotFound`
(`StorageRetention.cs`) recognises all of them without referencing any provider SDK: it walks the
exception and its inner exceptions for `FileNotFoundException`, `DirectoryNotFoundException`, or a
`StatusCode`, `HttpStatusCode` or `Status` property equal to 404, read by reflection. A missing exchange
file is then reported as `FILE_EXPIRED` when a lifecycle rule covers its prefix, else
`FILE_MISSING`. A new provider whose not-found exception matches none of these would surface as an
error instead.

### Complete listings

Adapter listing, the data source provider catalog and the S3 receiver all assume `ListAsync` returns
every key under the prefix. Before SimplyWorks.CloudFiles **10.0.4**, the S3 and Oracle providers
returned only the first 1,000 keys. Adapter listing reads all of `{AdapterPath}/`, every version of
every package, so on a store with enough adapter versions some adapters were missing from the lists.
With 10.0.4 or later the listing is complete, at one request per 1,000 keys, and the S3 receiver
lists its whole folder on every run before taking `BatchSize` files, so keep receiver folders drained.

### Existence checks

`ListAsync` matches by prefix, so every existence check compares keys exactly and requires a
non-empty object: the adapter catalog, the source reader, and the marketplace's check for an adapter
installed outside the registry (which until this fix refused `foo` when only `foo-bar` existed).

### Privacy

- Bitween writes with its own credentials and hands out its own sealed links, so the bucket should
  be private. `StorageAccess` fetches the probe file's unsigned `GetUrl` anonymously, and the Settings
  page warns when that works (the bucket is open to everyone).
- On Azure, privacy is set per container and the storage library creates the container public.
  `PrivateAzureContainer` uses the Azure SDK directly to switch it to private at startup.
- `XchangeService.FileUrl` falls back to the unsigned `GetUrl` when Bitween has no address to build a
  sealed link on (no public address, no request, no listening server). On a private bucket that URL
  doesn't open; in practice it only happens outside the web host.

### Memory

Exchange payloads are written and read as whole strings, and the archive, mirror and S3 receiver read
whole files. The Google provider also buffers every read in memory. Very large payloads cost their
full size in memory on the node handling them.

### Calls outside the abstraction

Two places use a provider SDK directly, so they work with one provider only:

| Where | Call | Why |
|---|---|---|
| `PrivateAzureContainer` | Azure `BlobContainerClient.GetAccessPolicyAsync` / `SetAccessPolicyAsync` | the interface has no access-policy call |
| `NativeS3Receiver` | `AmazonS3Client.CopyObjectAsync` for `DeleteMovesFileTo` | the interface has no server-side copy; downloading and re-uploading would move every byte through Bitween |

Both are candidates to move into the library when it gains those capabilities (copy is the first on
its [roadmap](https://github.com/simplify9/SW-CloudFiles/blob/main/docs/evolving-the-contract.md)).

## Package versions

| Release line | Framework | SimplyWorks.CloudFiles | Notes |
|---|---|---|---|
| `releases/r10.0` / `r10.0-develop` | net10.0 | 10.0.x | follows the library's 10.0.x releases |
| `releases/r8.0` | net8.0 | 8.1.1 | predates the library's client reuse and timeouts (8.1.11), the S3-compatible upload fix (10.0.2) and complete listings (10.0.4); the library publishes no further 8.1.x versions |

When bumping, read the library's [changelog](https://github.com/simplify9/SW-CloudFiles/blob/main/CHANGELOG.md)
for entries marked **Behaviour change**.
