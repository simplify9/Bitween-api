# The bitween CLI

`bitween` works with a Bitween from a terminal: signing in, as a person or as a service account;
adapters - writing, building, checking and publishing them with the Bitween contract; and configuration
as code - a Bitween's partners, information types, subscriptions, gateways and the rest, kept in YAML
files and applied with `bitween apply`.

It is built on [SW-Serverless](https://github.com/simplify9/SW-Serverless)'s tooling. `sw-serverless`
does the same for adapters of any application; `bitween` adds what is Bitween's: its four kinds,
its contract, its Python and Node packages, and publishing through Bitween's API.

## Install

Each release has a self-contained binary for Linux (x64, arm64, and musl x64 and arm64 for Alpine), macOS (x64, arm64) and
Windows (x64), with a `SHA256SUMS` file. Each release of Bitween from `releases/r10.0` releases the CLI with it, at Bitween's version, as
`cli-v<version>`; each build from `releases/r10.0-develop` releases a pre-release, `cli-v<version>-dev.<run>`. It needs nothing else
installed, apart from what the adapters you build need: the .NET SDK for .NET adapters, Python 3.12 or
later for Python ones (with pip when they have requirements), Node 22 or later for JavaScript and
TypeScript ones (with npm when they have dependencies).

```sh
curl -fsSL https://raw.githubusercontent.com/simplify9/Bitween-api/releases/r10.0/scripts/install-cli.sh | sh
```

That installs the latest release to `~/.local/bin`, after checking the download against `SHA256SUMS`;
`INSTALL_DIR` changes where, and `BITWEEN_CLI_VERSION=10.0.60` picks a version. The script never
picks a develop pre-release on its own, only when named: `BITWEEN_CLI_VERSION=10.0.61-dev.121`. It works on Linux and
macOS, on x64 and arm64, glibc or musl (Alpine). On Windows, download
`bitween-win-x64.zip` from the [releases](https://github.com/simplify9/Bitween-api/releases) and put
`bitween.exe` on your `PATH`.

From a clone: `dotnet run --project SW.Bitween.Cli -- <command>`.

## Signing in

```sh
bitween login https://bitween.example.com          # signs in through your browser
bitween whoami                                     # which Bitween, as whom, and your adapter permissions
bitween logout
bitween version
```

- `bitween login <url>` opens Bitween's sign-in page in your browser. Sign in there as you always do,
  with a password or with Microsoft, and confirm **Sign in the CLI**; the browser hands the CLI a one-time
  code on this machine's loopback, and the terminal is signed in. The code is good for two minutes and
  only with a secret the CLI keeps, so a code seen by anyone else is no use to them (PKCE).
- `--no-browser` is for a CLI on a machine without a browser, such as over SSH: it prints the address to
  open on any machine, and the page shows a code to paste back into the terminal.
- `--email` signs in with an email and password instead, asking for the password; `--password-stdin` reads
  it from standard input, for scripts:
  `echo "$BITWEEN_PASSWORD" | bitween login https://bitween.example.com --email ci@example.com --password-stdin`.
- `--profile <name>` keeps several Bitweens; it defaults to the address's host name. The last one signed
  in to is current; `--profile` on `whoami`, `logout` and the commands that act on a Bitween picks
  another, as does `BITWEEN_PROFILE`.
- `--insecure` skips the certificate check, for a Bitween on a self-signed development certificate only.
- Signing in over `http` to anything but this machine warns that the session or password is sent unencrypted.

Profiles are kept in `~/.config/bitween/cli.json` (`$XDG_CONFIG_HOME/bitween/cli.json` when that is set,
`%APPDATA%\bitween\cli.json` on Windows, or `BITWEEN_CLI_CONFIG`), readable by you alone on Linux and
macOS. They hold an access token and a refresh token, which renews the session as the browser's does,
for up to 30 days of inactivity. `logout` ends the session on the server, so its refresh token stops
working, and forgets the profile here. When the Bitween can't be reached it still forgets the profile and
says the session will end after 30 days unused.

Signing in through the browser works for every account, however it signs in, and needs nothing
configured. An account that must change its password changes it in the browser first; the CLI can't be
signed in as it until then.

### As a service account

A job with no one to sign in, such as a pipeline or a Kubernetes CronJob, signs in as a **service account**
instead (Team > Service accounts in Bitween). Issue the account a token there, then give the job:

```sh
export BITWEEN_URL=https://bitween.example.com
export BITWEEN_TOKEN=bwt_...           # or BITWEEN_TOKEN_FILE=/var/run/secrets/bitween/token
bitween whoami
```

With no `--profile`, a token in the environment is used over any profile, nothing is written to disk, and the
token is traded for a short session that is renewed by trading it again. `BITWEEN_INSECURE=1` skips the
certificate check, like `--insecure`. A service account can do what its roles allow; give it only what the job
needs. Tokens expire (90 days unless set, a year at most); an account holds two at a time so a new one can be
put in place before the old one is revoked, and the dashboard lists tokens within two weeks of expiring.

## Adapters

```sh
bitween adapter init AcmeOrders --kind handler --lang python
cd AcmeOrders
cp settings.example.json settings.json
bitween adapter test --settings settings.json
bitween adapter build
bitween adapter publish bin/serverless/acme.orders-0.1.0.zip
bitween adapter promote acme.orders 0.1.0
```

| Command | What it does |
|---|---|
| `adapter init <Name>` | Writes a new adapter in a folder named after it: its code, `adapter.json`, `settings.example.json`, a README and a `.gitignore`. `--kind handler\|mapper\|validator\|receiver` (handler), `--lang dotnet\|python\|node\|typescript` (dotnet), `--id` (from the name: AcmeOrders → acme.orders), `--dir` (the current folder). |
| `adapter build [folder]` | Builds the package: the adapter, its manifest written from what its code declares, its source under `source/`, and for Python and Node the SDK and Bitween's kinds vendored in. `-o` the output folder (`bin/serverless` in the project), `--no-source`, `--allow <files>` for secret-scan false positives, separated by spaces, `--dry-run` to list the source it would carry and build nothing. |
| `adapter test [package]` | Starts it as Bitween would and checks it against the Bitween contract: its description, its settings against its manifest, each kind's methods called with the contract's examples. A project folder is built first. `--settings file.json`, `--allow-delete` to let a receiver's `DeleteFile` run, `--timeout` seconds per call (60). |
| `adapter run [package] --call <Command>` | Calls one command and prints its result: `--input` JSON, text or `@file`, `--settings`, `--timeout` (60). |
| `adapter publish <zip>` | Publishes through the signed-in Bitween. `-v major\|minor\|patch\|1.2.3` (the package's own version unless given), `--current` to make it the version that runs, `--notes`. Needs `adapter-source.operate`. |
| `adapter promote <id> <version>` | Makes a published version current. Needs `adapter-source.operate`. |
| `adapter versions <id>` | Lists published versions, with when and by whom each was published; `*` marks the current one. Needs `subscriptions.view`. |
| `adapter withdraw <id> <version>` | Takes a version out of use: still listed, never pinned or made current. Needs `adapter-source.operate`. |

### Straight to storage

`publish`, `promote`, `versions` and `withdraw` act on storage directly when given `-p` (or
`SWSL_PROVIDER` is set) and no `--profile`, with the same flags and environment variables as `sw-serverless`:
`-p s3|as|oc|gc|local`, `-b` bucket, `-a`/`-s` keys, `-u` service URL, `-c` a config file, or `SWSL_*`
variables. The catalog then records the publisher from `SWSL_PUBLISHED_BY`, `GITHUB_ACTOR` or the local
user name, and nothing reaches Bitween's audit trail.

## Testing mappings

`bitween mapping test` maps sample documents with subscriptions' mappings as saved on the Bitween, and
compares each output with the one expected. It runs the same preview the mapping editor does, which is
the pipeline's own read, map and write; nothing is delivered. Run it in CI after `bitween apply`, so a
mapping change that breaks a partner's document fails the build.

```yaml
# mappings.yaml
cases:
  - name: An order maps to the warehouse's shape
    subscription: Orders to warehouse
    input: samples/order.json
    expected: samples/order.expected.json
    partner: Acme marketplace        # whose values to map with; the subscription's own when left out
```

```sh
bitween mapping test mappings.yaml            # PASS or FAIL per case; exit 1 when any fails
bitween mapping test mappings.yaml --update   # write each output as its expected file, to start or refresh them
```

Paths are relative to the file. JSON is compared as JSON, so spacing and key order don't count, and a
failure names the first path that differs, as `$.customer is "acme"; "ACME" expected.` XML is compared as
XML. The rules-based mapper and the legacy JSON mapper can be tested this way; a custom mapper is tested
with `bitween adapter run`. Needs `subscriptions.edit`, which the preview asks for, so a CI account needs it too.

## Configuration as code

```sh
bitween export -o acme.yaml --name acme     # start from what a Bitween has
bitween plan -f acme.yaml                   # what would change; changes nothing
bitween apply -f acme.yaml                  # make it so
bitween apply -f config/ --prune            # a folder of files; also remove what was dropped
```

A configuration is one YAML file or a folder of them, merged:

```yaml
apiVersion: bitween/v1
kind: Configuration
metadata:
  name: acme
informationTypes:
  - name: Marketplace order
    code: MKT_ORDER
    format: Json
    validationSchema: ${file:schemas/order.json}
partners:
  - name: Acme marketplace
    properties: { region: eu }
    keys:
      main: ${secret:MARKETPLACE_KEY}
notificationChannels:
  - name: Ops email
    handler: NativeEmailMessageHandler
    properties: { To: ops@acme.example }
subscriptions:
  - name: Orders to warehouse
    type: GatewayApiCall
    informationType: Marketplace order
    retryPolicy: Partner outage
    handler:
      adapter: NativeHttpHandler        # a published adapter's id, with version: 1.2.0 to pin it
      properties:
        Url: https://warehouse.example.com/orders
        ApiKey: ${secret:WAREHOUSE_KEY}
    notifications:
      - channel: Ops email
        onFailure: true
        onQuiet: true
apiGateways:
  - name: Orders inbound
    urlName: orders
    partners:
      - partner: Acme marketplace
        subscription: Orders to warehouse
adapters:
  - package: adapters/acme.orders-1.2.0.zip
    current: true
```

| Section | Named by | Notes |
|---|---|---|
| `subscriptionCategories` | `code` | |
| `workGroups` | `name` | |
| `informationTypes` | `name` | `format`, `promotedProperties` as a mapping |
| `partners` | `name` | `properties`, `secretProperties`, `loginIdentity`, `keys` as name: value |
| `globalValues` | `id` | `values` as a mapping |
| `connections` | `name` | `grant`, `authorizeUrl`, `tokenUrl`, `clientId`, `clientSecret`, `scopes`, `authorizeParameters`, `tokenAuth`, `username`, `password`; see [Connections](adapters.md#connections). An approve-once (`AuthorizationCode`) connection still has to be authorized in Bitween after apply makes it; plan says so. |
| `notificationChannels` | `name` | `handler`, `properties` |
| `retryPolicies` | `name` | `alertChannel`; `groups` as Bitween writes them, each with its own `alertChannel`; groups keep their ids by name |
| `dataSources` | `name` | `adapter`, `kind`, `properties` |
| `dataSourceStatements` | `dataSource` and `name` | `sql`, `workGroup` |
| `adapters` | the package | `package:` a zip, published unless that version already is; or `install:` a connector's id with `version:`, installed from the [marketplace](adapters.md#installing-from-the-marketplace) unless this Bitween has that version. `current: true` makes it the one that runs. |
| `subscriptions` | `name` | `type`, `informationType`, `partner`, `receiver`/`validator`/`mapper`/`handler` blocks, `paused`, `expectTraffic` compared whole (a member left out is unset), `notifications` compared whole (a tick left out is off) |
| `apiGateways` | `urlName` | `partners` attached, each `{ partner, subscription }` |
| `busGateways` | `name` | `routes`, each `{ subscription, partner, matchExpression }` |
| `notifiers` | `name` | legacy, kept for those still in use: `handler`, `properties`, `subscriptions` |

- **Names, not ids.** Items refer to each other by name, so one file works on any Bitween. A name Bitween has
  twice can't be referred to; plan says which.
- **Only what the file says.** A field left out is left as it is. A mapping that replaces everything, such as
  an adapter's `properties` or a partner's `keys`, is compared and applied whole. A subscription's `type`,
  `informationType`, `partner` and `aggregationFor` can't change once it exists; plan refuses rather than
  guess.
- **Alerts and notifications** name a notification channel: `alertChannel` on a retry policy or a group, and
  `channel` in each of a subscription's `notifications`, with the ticks `onFailure`, `onBadResult`,
  `onSuccess` and `onQuiet`. A file from before channels, with an `alertHandler`, is refused with what to
  write instead.
- **Secrets.** `${secret:NAME}` comes from the variable `NAME`, or the file `NAME` in `--secrets-dir`
  (`BITWEEN_SECRETS_DIR`), such as a mounted Kubernetes secret. `${env:NAME}` and `${file:path}` (relative
  to the file) work too, and `${param:NAME}` reads a value given with `--set NAME=value` (once per value) or a
  `--values` file of `NAME: value` lines, as a [kit](#kits)'s parameters are. Secrets are never printed. Bitween doesn't show the secrets it holds, so an unchanged
  secret can't be told from a changed one: it is sent again whenever anything else about that item changes.
  A partner's key is compared by the five characters Bitween shows, and replaced when it no longer starts
  with them.
- **Order.** Apply works through the sections in the order above, and within subscriptions puts one that
  another names first. It stops at the first refusal, with what was done before it kept; running it again
  carries on.
- **Prune.** Apply remembers what it has made, per `metadata.name`, in a global values set named
  `bitween-apply.<name>`. `--prune` removes what that lists and the file no longer does, last and in reverse
  order; nothing else, so configuration made by hand alongside is never touched.
- **Export** writes every section (or `--only partners,subscriptions`), leaves out defaults and built-in
  items, and names each secret it couldn't read as `${secret:...}`, listing them so they can be supplied.

Apply needs the permissions of what it changes, and `global-values.create` and `global-values.edit` to
remember what it made.

### Kits

A kit is connectors plus the configuration that makes them useful: one job between two systems, such
as Salesforce contacts both ways. It is a folder:

```
kit.yaml     what it is, the connectors it needs, the parameters it asks for
bundle/      configuration files, as apply takes them, reading the parameters as ${param:NAME};
             files they read with ${file:...} (API descriptions, schemas, mappings) go in subfolders
samples/     sample documents, optional
```

```yaml
apiVersion: bitween/v1
kind: Kit
metadata:
  id: s9.salesforce.contacts
  version: 1.0.0
  displayName: Salesforce contacts
  summary: Contacts both ways between Salesforce and your system.
connectors:
  - { id: s9.openapi.receiver, version: 1.0.0 }
parameters:
  - name: InstanceUrl
    description: Your Salesforce org's address.
    example: https://acme.my.salesforce.com
  - name: ClientSecret
    description: The connected app's consumer secret.
    secret: true
  - name: Label
    description: Put before the names of what it makes.
    default: Salesforce
```

A parameter is required unless it has a `default` or says `required: false`. A `secret: true` one is kept
out of what's printed, and can also come from a file of its name in `--secrets-dir`.

A kit reads nothing but its parameters and its own files: `${env:...}` and `${secret:...}` are refused, and
`${file:...}` must name a file inside `bundle/`. Bitween installs kits from the Marketplace tab inside its
own process, where the environment holds its signing key and database password, so a kit can't name them.
Plain `plan` and `apply` files keep every reference.

```sh
bitween kit test kits/s9.salesforce.contacts                 # checks it, without a Bitween
bitween kit test kits/s9.salesforce.contacts --plan --set ...   # and what it would do on one
bitween kit install kits/s9.salesforce.contacts --set InstanceUrl=https://acme.my.salesforce.com \
  --set ClientSecret=... --dry-run
bitween kit install kits/s9.salesforce.contacts --values acme.yaml --name salesforce-acme
bitween kit build kits/s9.salesforce.contacts                # a zip for the registry
```

- **test** checks kit.yaml, that every `${param:...}` the bundle reads is declared (and warns about one
  declared and never read), that every adapter it runs is built in or one of its connectors, and warns
  about a connector its subscriptions don't pin to the version the kit names. With `--plan` it plans the
  kit on the signed-in Bitween.
- **install** installs its connectors from the marketplace at the versions it names (not made current,
  so subscriptions already on another version keep it), then applies the bundle with the parameters
  filled in. It is applied as a configuration named after the kit, or `--name`, so installing again
  changes only what differs, `--prune` removes what the kit dropped, and a second `--name` installs it a
  second time, for a second org; a `Label`-style parameter keeps the two apart.
- **build** checks it and zips it with an `adapter.json` of kind `kit`, which the registry reads: the kit's
  files under `source/` with their hashes, and its connectors and parameters in a `kit` section.

A kit published to the registry is also installed from Bitween's [Marketplace tab](adapters.md#installing-from-the-marketplace),
which asks for its parameters in a form and shows the same plan.
Before review, the registry plans each kit published on a reference Bitween (`POST /api/kits/plan` with
its examples standing in for parameters), so give every required parameter without a default an `example`
that plans.

## Publishing to the registry

A publisher sends adapters and kits to a [Bitween Registry](adapters.md#installing-from-the-marketplace),
where they wait for review. Publishing signs the package's SHA-256 with the publisher's Ed25519 key; the
registry checks the signature against the public key it holds for the publisher, then a reviewer approves it
and the registry countersigns it.

```sh
echo "$REGISTRY_TOKEN" | bitween registry login https://registry.bitween.io --key-file ~/keys/s9.pem
bitween registry publish connectors/s9.openapi/bin/serverless/s9.openapi-1.0.0.zip
bitween registry publish kits/s9.zoho.contacts/bin/s9.zoho.contacts-1.0.0.zip
bitween registry status s9.openapi            # every version: in review, published, rejected, revoked
bitween registry status s9.openapi 1.0.0      # one version's checks and the review's notes
bitween registry whoami
bitween registry logout
```

- **login** reads a publish token, issued on the registry's Tokens page, from standard input, checks it with
  the registry, and keeps it with the registry's address in the CLI's configuration file, readable by you
  alone. `--key-file` keeps the path of the signing key, never the key.
- **publish** reads the id and version from the zip's `adapter.json`, signs it, and sends it. It prints every
  check the registry ran. It exits `0` when the version is in review, `1` when a check failed (the failed ones
  are printed) and `2` when that version is already in the registry, so CI can tell a missing version bump
  from a broken package.
- The key is an unencrypted Ed25519 private key in PEM, as `openssl genpkey -algorithm ed25519` writes it.
  `--key-file` on publish, then `BITWEEN_SIGNING_KEY` (its PEM text), then `BITWEEN_SIGNING_KEY_FILE`, then
  the path login kept, in that order. A key that can't be read is named by where it came from, never shown.
- In CI, skip login: `BITWEEN_REGISTRY_URL` and `BITWEEN_REGISTRY_TOKEN` win over what login kept.

The signature is the one `scripts/sign.sh` in Bitween-Connectors makes, byte for byte.

What the registry checks before a version goes to review, each check printed by `publish`:

- **the package:** its manifest, that its id is in the publisher's namespace, the oldest Bitween it works with,
  and that it carries its source, every file matching the hash its manifest records;
- **the signature,** against a live key of the publisher;
- **secrets:** the scan `bitween adapter build` runs, over every file the publisher wrote. It reports where,
  never the value;
- **dependencies:** what the package vendors (Python, Node and .NET), asked of the [OSV](https://osv.dev)
  vulnerability database. A known critical vulnerability refuses it; others are listed for the reviewer;
- **licences:** a dependency licensed under AGPL, GPL, SSPL, BUSL, a non-commercial Creative Commons licence or
  the Commons Clause refuses it, unless it may be taken under another licence. LGPL and permissive licences pass;
- **a kit's connectors,** which must be in the registry at the versions it names.

The registry runs no package code. The contract check is `bitween adapter test`, which a publisher runs in its
CI before publishing; Simplify9's connectors aren't published without passing it. Then a reviewer at Simplify9
reads the version, with the files changed since the previous one marked, approves it, and the registry
countersigns it.

### Exit codes

`0` when the command did what was asked, `1` otherwise - a failed check, a refused publish, a problem
in the arguments - with the reason printed. `bitween plan --detailed-exitcode` exits `2` when there are
changes to apply, and `bitween registry publish` exits `2` when the version is already in the registry. `bitween adapter test` prints each check as `PASS`, `FAIL`
or `SKIP`; with `--json` it prints only the report, `{ passed, checks: [{ name, outcome, detail, call }] }`, where
`call` marks a check that called one of the adapter's commands, which needs its settings to pass.

The CLI is `SW.Bitween.Cli`, built on `SW.Bitween.Adapters.Tooling`, which the adapter editor in Bitween
also uses. It is released by `.github/workflows/cli-release.yml`; see [Deployment](deployment.md#cicd).
