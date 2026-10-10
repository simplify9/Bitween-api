# The bitween CLI

`bitween` works with a Bitween from a terminal. This release covers adapters — writing, building,
checking and publishing them with the Bitween contract — and signing in to a Bitween, which the
commands that change it use.

It is built on [SW-Serverless](https://github.com/simplify9/SW-Serverless)'s tooling. `sw-serverless`
does the same for adapters of any application; `bitween` adds what is Bitween's: its four kinds,
its contract, its Python and Node packages, and publishing through Bitween's API.

## Install

Each release has a self-contained binary for Linux (x64, arm64, and musl x64 and arm64 for Alpine), macOS (x64, arm64) and
Windows (x64), with a `SHA256SUMS` file. Releases are tagged `cli-v<version>`. It needs nothing else
installed, apart from what the adapters you build need: the .NET SDK for .NET adapters, Python 3.12 or
later for Python ones (with pip when they have requirements), Node 22 or later for JavaScript and
TypeScript ones (with npm when they have dependencies).

```sh
curl -fsSL https://raw.githubusercontent.com/simplify9/Bitween-api/releases/r10.0/scripts/install-cli.sh | sh
```

That installs the latest release to `~/.local/bin`, after checking the download against `SHA256SUMS`;
`INSTALL_DIR` changes where, and `BITWEEN_CLI_VERSION=10.0.60` picks a version. Staging builds are
pre-releases tagged `-stg`, such as `cli-v10.0.61-stg`; the script never picks one on its own, only when
named: `BITWEEN_CLI_VERSION=10.0.61-stg`. It works on Linux and
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

### Exit codes

`0` when the command did what was asked, `1` otherwise — a failed check, a refused publish, a problem
in the arguments — with the reason printed. `bitween adapter test` prints each check as `PASS`, `FAIL`
or `SKIP`.

The CLI is `SW.Bitween.Cli`, built on `SW.Bitween.Adapters.Tooling`, which the adapter editor in Bitween
also uses. It is released by `.github/workflows/cli-release.yml`; see [Deployment](deployment.md#cicd).
