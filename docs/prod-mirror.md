# Prod mirror

A staging Bitween can copy a prod Bitween's setup, with every value faked, so an upgrade can be tried on the staging before it reaches prod. The **Prod mirror** page on the staging shows how close it is to prod, what is left to do, and copies prod's setup onto it.

Prod is only read, through its own API, with a view-only login. Nothing is installed or changed on prod.

## What it needs

- **Prod on r10.0 or later.** A prod on r8, on Infolink (r6 and before), or on a build changed for one client is refused with the reason. Move such a client to r10 first.
- **A staging marked as a mirror.** Copying replaces the staging's setup, so it only runs on a Bitween whose deployment sets `Bitween__Mirror__Target=true`. A prod never sets it, so a prod can't be overwritten from its Settings page. Without it the page still compares, but doesn't copy.
- **A view-only login on prod**, one of:
  - a service account with the built-in **Viewer** role, and a token issued to it (**Administration, Service accounts** on prod). Releases that have service accounts take this;
  - on r10.0, which has none, the email and password of a member with the Viewer role.

Enter prod's address and the login on the page (**Connect to prod**), or in **Settings, Prod mirror**. The token and password are stored encrypted, which needs `Bitween:SettingsEncryptionKey`.

## What is copied, and how

Everything a configuration file can hold is copied by name: subscription categories, work groups, information types, partners, global values, connections, notification channels, retry policies, data sources and their SQL statements, subscriptions, API gateways, bus gateways and legacy notifiers. A kind prod's release doesn't have is left out and says so. The copy goes through the same engine as [`bitween apply`](cli.md).

Values are handled the moment they arrive, before anything is saved, so the staging never holds a real value of prod's:

| Kind of value | Examples | On the staging |
|---|---|---|
| How a flow behaves | formats, schedules, filters and match expressions, mapping rules, methods, delimiters, file name patterns, `{{globals.…}}` tokens | Copied as it is |
| Addresses | hosts, URLs, email addresses, connection strings' servers, an API gateway's issuer | A mock address under `mirror.invalid`, which resolves nowhere. The same address is the same mock wherever prod names it |
| Secrets | anything prod shows as `__private__`, passwords in URLs and connection strings | A stand-in, the same on every copy |
| Partner properties and global values | account numbers, codes, names | A fake of the same shape: digits stay digits, letters keep their case, dates stay dates in their format, GUIDs stay GUIDs, JSON keeps its keys |
| Partner API keys | | New ones, the same on every copy |

The same value always becomes the same fake, so a code named in two places still matches, and copying again changes nothing. A partner property or global value that is a setting rather than data, such as a time zone or a date format, can be kept as it is under **Faked values**. Addresses, secrets and values named like secrets (`apiKey`, `password`, `token` and similar) are never kept.

Copied subscriptions arrive paused, and data sources and bus gateways switched off, so nothing on the staging runs on its own.

Legacy setups are copied as prod has them: Internal and ApiCall subscriptions, legacy notifiers, and a response handed to an Internal or ApiCall subscription. Bitween keeps such a response link on an item that already has it but refuses a new one, and a copy is a new save. So the copy alone may recreate it: its calls carry a key it opens for the length of the copy, on a Bitween marked as a mirror. Someone editing on the staging never carries one and meets the same rule as on prod.

A field prod sends that this release doesn't know, from a newer or a customised build, can't be copied. The page names each one and the items that have it set, rather than dropping it unseen.

Prod may hold several items of a kind under one name. Each after the oldest is named apart on the staging: `test`, `test (2)`, `test (3)`. References to them still point at the right one.

## Samples of prod's traffic

**Take samples** takes a few of prod's recent exchanges for each subscription: up to five, mostly ones that worked, with one the partner answered badly and one that failed when prod has them, never a retry or one still running. For each it keeps what came in, what Bitween sent and what the partner answered, and how the exchange ended. The rehearsal runs them through the staging's flows.

Each file is faked the moment it arrives, in its own format, before it is saved:

| Format | Kept as it was | Faked |
|---|---|---|
| JSON | keys, structure, `true`, `false`, `null` | strings, and numbers' digits |
| XML | element and attribute names, namespaces, `xsi:type`, the declared encoding | text, CDATA, comments, attribute values; XML escaped inside an element is faked as XML |
| CSV | the delimiter, quoting, line endings | every field |
| Excel | sheets, styles, formulas, booleans | shared strings, numbers, text in cells, comments, the author |
| HTML and other text | tags, entities, punctuation, spacing | the words between them |

Values are faked the way partner properties are, so the same value is the same fake in a file and in the setup. A word the staging's setup itself names stays as it is wherever it turns up: a filter's `'SHIPPED'`, the `ORD` of a file pattern `ORD_*.csv`, a global value's key. So a faked file takes the path the real one took, and keeping such a word tells the staging nothing it doesn't already hold. File names are faked too, keeping their extension.

A file that can't be faked safely isn't kept, and the page says why: PDFs, archives, Word files, EDI files, workbooks with pictures, charts or pivot tables, binary files and files over 5 MB. Of prod's error only the partner's HTTP status and the kinds of error it names, such as `HttpRequestException`, are kept: the message can hold prod's data, so the page links to the exchange on prod to read it.

Prod is read gently: one subscription at a time, one page of its recent exchanges, then each file. Taking samples again replaces the ones taken before.

## Calls the staging answers

On a mirror, the staging answers some of the calls its flows make itself, so nothing on the staging reaches prod's partners:

- **Test addresses**: every address the copy put in place of one of prod's.
- **Hosts the staging's setup doesn't name**: an address written inside an adapter's code, say. It may be one of prod's partners, so it is answered rather than reached.

A host the setup names, such as a team's own staging API, is called for real. So is any host listed under **Settings, Prod mirror, Hosts called for real**, for an address inside an adapter that is safe to reach from the staging.

A login (an OAuth token request, or a login URL a subscription signs in at) gets a token of the staging's own, where the subscription's settings say its reply keeps one. Any other call gets the newest answer prod's partner gave a subscription that uses the address, as the samples keep it, faked; with no sample, an empty `200`. **Calls staging answered** on the page lists each host, how many calls it answered and what it answered last.

Bitween's own adapters are answered inside Bitween. Adapters that run in their own process reach the staging through a catcher it runs on `127.0.0.1`: when it starts, the staging sets `HTTP_PROXY`, `HTTPS_PROXY` and `NO_PROXY` for the processes it starts, and points `SSL_CERT_FILE`, `REQUESTS_CA_BUNDLE`, `CURL_CA_BUNDLE` and `NODE_EXTRA_CA_CERTS` at a certificate authority it makes for that run, so .NET, Python, Node and curl trust the answers. Bitween's own calls, to prod and to its storage, don't go through the catcher. Nothing needs setting up.

What the catcher can't reach: an adapter that turns its HTTP proxy off, or checks certificates its own way, fails its calls to test addresses instead of being answered, and the page lists the call as failed. Calls that aren't HTTP, such as SFTP or mail written inside an adapter, aren't caught yet.

## Adapters

Built-in adapters come with the release. One that prod uses and this release doesn't have is flagged, since it would stop working after the upgrade. Marketplace adapters are installed by the copy at the version prod runs. Simplify9's database and broker adapters (`bitween.db.…`, `bitween.bus.…`) are on the marketplace too; when prod doesn't say which version it runs, the page asks for one to be installed from the **Marketplace** tab. Custom adapters can't be read from prod: the page names each one, with the version prod runs, to upload on the **Adapters** page. Subscriptions that use one wait for it rather than fail.

## The page

It answers first: **Ready to test the upgrade**, or **Not ready to test yet**, with how many of prod's items the staging has as prod has them. Below it, everything is in one of four groups, and every item named opens on the staging or on prod:

- **Needs you**: what only a person can do, such as installing an adapter, each with where it is done.
- **Copy prod's setup**: what copying will do, and the button. It creates what the staging lacks and updates what differs, carries on past an item that fails, and lists each one with Bitween's reason. Tick **Also remove** to delete what only the staging has.
- **Samples of prod's traffic**: how many samples the staging holds and when they were taken, and the button that takes them again. While it runs, how far it has got.
- **Not covered by the test**: what is on prod but not on the staging, or not all of it: items waiting for an adapter, items the staging couldn't create, and fields this release doesn't know.
- **Different on staging, on purpose**: paused subscriptions, switched-off data sources and bus gateways, fake values, test addresses, stand-in secrets and names told apart. None of it is a problem.

Then the details, folded: **Prod's setup, item by item** (per kind, what is the same, differs, is missing or is only on the staging), **Samples, subscription by subscription** (how each ended on prod, a link to each faked file and to the exchange on prod), **Adapters**, **Fake values** (where a value can be kept as it is) and **Test addresses** (each with what uses it).

When the staging can't create one of prod's items, the page says so, with what Bitween said. That is all it knows: an upgrade doesn't create items again, so a refusal here says nothing about how prod will behave.

## Not covered yet

- Running prod's flows on the staging and comparing the results with prod's. Until then the samples are only kept.
- SFTP, FTP and mail test servers. Until then those addresses lead nowhere.
- Runtime settings, roles, members and service accounts, which differ between environments on purpose.
