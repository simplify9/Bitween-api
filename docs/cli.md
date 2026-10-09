# The bitween CLI

`bitween` works with a Bitween from a terminal. This release covers adapters — writing, building,
checking and publishing them with the Bitween contract — and signing in to a Bitween, which the
commands that change it use.

It is built on [SW-Serverless](https://github.com/simplify9/SW-Serverless)'s tooling. `sw-serverless`
does the same for adapters of any application; `bitween` adds what is Bitween's: its four kinds,
its contract, its Python and Node packages, and publishing through Bitween's API.

## Install

Each release has a self-contained binary for Linux (x64, arm64, musl x64), macOS (x64, arm64) and
Windows (x64), with a `SHA256SUMS` file. It needs nothing else installed, apart from what the adapters
you build need: the .NET SDK for .NET adapters, Python 3.12 or later for Python ones, Node 22 or later
for JavaScript and TypeScript ones.

```sh
curl -fsSL https://raw.githubusercontent.com/simplify9/Bitween-api/main/scripts/install-cli.sh | sh
```

That installs the latest release to `~/.local/bin`; `INSTALL_DIR` changes where, and
`BITWEEN_CLI_VERSION=10.0.60` picks a version. On Windows, download `bitween-win-x64.zip` from the
[releases](https://github.com/simplify9/Bitween-api/releases) and put `bitween.exe` on your `PATH`.

From a clone: `dotnet run --project SW.Bitween.Cli -- <command>`.

## Signing in

```sh
bitween login https://bitween.example.com          # asks for your email and password
bitween whoami                                     # which Bitween, as whom, and your adapter permissions
bitween logout
```

- `--email` gives the email; `--password-stdin` reads the password from standard input, for scripts:
  `echo "$BITWEEN_PASSWORD" | bitween login https://bitween.example.com --email ci@example.com --password-stdin`.
- `--profile <name>` keeps several Bitweens; it defaults to the address's host name. The last one signed
  in to is current; `--profile` on any command picks another, as does `BITWEEN_PROFILE`.
- `--insecure` skips the certificate check, for a Bitween on a self-signed development certificate only.

Profiles are kept in `~/.config/bitween/cli.json` (`%APPDATA%\bitween\cli.json` on Windows, or
`BITWEEN_CLI_CONFIG`), readable by you alone. They hold an access token and a refresh token, which
renews the session as the browser's does, for up to 30 days of inactivity. Accounts that sign in with
Microsoft can't sign in from the CLI yet.

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
| `adapter init <Name>` | Writes a new adapter. `--kind handler\|mapper\|validator\|receiver` (handler), `--lang dotnet\|python\|node\|typescript` (dotnet), `--id` (from the name: AcmeOrders → acme.orders), `--dir`. |
| `adapter build [folder]` | Builds the package: the adapter, its manifest written from what its code declares, its source under `source/`, and for Python and Node the SDK and Bitween's kinds vendored in. `-o` the output folder, `--no-source`, `--allow <file>` for a secret-scan false positive, `--dry-run`. |
| `adapter test [package]` | Starts it as Bitween would and checks it against the Bitween contract: its description, its settings against its manifest, each kind's methods called with the contract's examples. A project folder is built first. `--settings file.json`, `--allow-delete` to let a receiver's `DeleteFile` run, `--timeout`. |
| `adapter run [package] --call <Command>` | Calls one command: `--input` JSON, text or `@file`, `--settings`. |
| `adapter publish <zip>` | Publishes through the signed-in Bitween. `-v major\|minor\|patch\|1.2.3` (the package's own version unless given), `--current` to make it the version that runs, `--notes`. Needs `adapter-source.operate`. |
| `adapter promote <id> <version>` | Makes a published version current. Needs `adapter-source.operate`. |
| `adapter versions <id>` | Lists published versions; `*` marks the current one. |
| `adapter withdraw <id> <version>` | Takes a version out of use: still listed, never pinned or made current. Needs `adapter-source.operate`. |

### Straight to storage

`publish`, `promote`, `versions` and `withdraw` act on storage directly when given `-p` (or
`SWSL_PROVIDER` is set), with the same flags and environment variables as `sw-serverless`: `-p s3|as|oc|gc|local`,
`-b` bucket, `-a`/`-s` keys, `-u` service URL, `-c` a config file, or `SWSL_*` variables. Bitween then
doesn't see who published, and nothing reaches its audit trail.

### Exit codes

`0` when the command did what was asked, `1` otherwise — a failed check, a refused publish, a problem
in the arguments — with the reason printed.
