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

**Take samples** takes a few of prod's recent exchanges for each subscription: up to five, mostly ones that worked, with one the partner answered badly and one that failed when prod has them, never a retry or one still running. Each comes with its **arrival**: the thing that came in on prod and every exchange it led to there. That is a partner's call through a gateway or the partner API, a document routed to the subscriptions that want it, a file a receiver read, or an aggregation, followed by the answers it handed on to response subscriptions and the messages bus gateways took. For each exchange it keeps what came in, what Bitween sent and what the partner answered, and how the exchange ended.

Prod records how its exchanges are linked: a document's subscriptions share its correlation id and its input, and an answer handed on or a message names the exchange it came from. The samples follow those links only. Each search on prod is bound to the minutes around the exchange it starts from, which prod has indexed, so a big prod isn't searched end to end. An arrival keeps up to 25 exchanges. A subscription whose handler reads values from the exchange that started it (`{{source.…}}`) runs only inside that exchange's arrival; when prod doesn't say which exchange that was, the page says so.

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

An aggregation also keeps a few of the files it rolled up, faked, read through prod's own links to them. Staging never fetches an address that prod's data merely names.

Prod is read gently: one subscription at a time, one page of its recent exchanges, then each file. Taking samples again replaces the ones taken before.

## Rehearsal

**Rehearse** sends each arrival in on the staging the way it came in on prod, with prod's input as the staging keeps it, faked. The staging then runs everything after it by itself, as prod did:

| It came in on prod as | The rehearsal sends it in |
|---|---|
| A partner's call to an API gateway | As that partner, to the staging's gateway: with the key the copy gave the partner, or, for a gateway that checks tokens, a token the staging signs as the partner's login server would |
| A partner's call to the partner API | As that partner, to the staging's partner API |
| A document | To the staging, which routes it to the subscriptions that want it |
| A file a receiver read | Handed straight to the receiving subscription; reading it from the server comes with the test servers |
| An aggregation | **Aggregate now**, rolling up what the staging ran of the subscription it aggregates; when it ran none, the files prod rolled up, faked |
| An exchange started by hand | Started the same way |

The staging's subscriptions stay paused, and its data sources and bus gateways switched off, so nothing runs on its own. Only the rehearsal's traffic gets through: it carries a correlation id of its own, which every exchange it leads to carries on, across the bus too, and calls through a gateway carry a key the rehearsal opens for as long as it runs. That traffic passes paused subscriptions, goes through the bus gateways prod has on, and is never taken for a duplicate of the rehearsal before it. Anyone else's traffic meets the staging as it is.

When an exchange calls a partner, the staging answers with what prod's partner answered its twin, faked: the same body, `400` for an answer prod's partner gave with an error, the status prod's error names, or a call that fails to connect when prod's never got through. Arrivals run one at a time, so each exchange gets its own twin's answer, and they appear on the staging's **Exchanges** page under their correlation id.

Each of the staging's exchanges is paired with its twin on prod (the same subscription, in the order they started) and compared, and gets one of three verdicts:

- **Different**: it ended another way (worked, answered badly, failed), or what it sent or got back changed its format, lost or gained a field, has a list of another length, or writes a value another way: a date in another format, a number turned text, a value left empty. An exchange one side ran and the other didn't is different too, and so is a call the staging's gateway turned away.
- **Check**: only values' text changed, and each kept its look. Fake data explains that when a mapping works values out, such as a sum or a cut; a wrong field taken doesn't, so it is worth a look.
- **Same**: nothing changed.

An arrival's verdict is the worst of its exchanges'. Both sides go through the same faker, so a value that is only faked reads the same on both. Opening an arrival shows how it came in, then its exchanges, the ones that differ first. Opening an exchange shows what changed first, prod beside staging, and folds what is the same into one line. A changed file opens field by field, and side by side. **What it used** compares the subscription, its information type, the partner it ran as, the global values it names and its adapters with prod's: prod's setup as it is today, faked as the copy fakes it, since prod's API doesn't give the setup an exchange ran with, only its adapters. These only explain a verdict. So do **Calls it made**: a call to a host sent for real (one the staging's setup names, the staging itself or this machine) gets that host's answer, not prod's partner's, and the page says so.

An arrival doesn't run when it can't be sent in the way it came in on prod, and the page says why: a subscription, partner, gateway or information type the staging doesn't have yet, or an input that isn't kept.

Taking samples and rehearsing don't run at the same time. A rehearsal replaces the one before it.

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
- **Rehearse prod's traffic**: how many samples the staging holds, and how the last rehearsal compared, with the buttons that take samples and rehearse. While either runs, how far it has got.
- **Not covered by the test**: what is on prod but not on the staging, or not all of it: items waiting for an adapter, items the staging couldn't create, and fields this release doesn't know.
- **Different on staging, on purpose**: paused subscriptions, switched-off data sources and bus gateways, fake values, test addresses, stand-in secrets and names told apart. None of it is a problem.

Then the details, folded: **Prod's setup, item by item** (per kind, what is the same, differs, is missing or is only on the staging), **Arrival by arrival** (each arrival with its verdict, how it came in, prod's ending beside the staging's, the staging's exchanges and prod's, the different ones first), **Calls staging answered**, **Adapters**, **Fake values** (where a value can be kept as it is) and **Test addresses** (each with what uses it).

When the staging can't create one of prod's items, the page says so, with what Bitween said. That is all it knows: an upgrade doesn't create items again, so a refusal here says nothing about how prod will behave.

## Not covered yet

- SFTP, FTP and mail test servers. Until then those addresses lead nowhere, and a receiver's rehearsal starts from the file prod's receiver read.
- Databases: a flow that reads or writes one, and data sources. They will answer with what they returned on prod, with a note that a test database tests more.
- Files that can't be faked, such as PDFs: they will get stand-ins of the same kind, with a note asking for real test files.
- Comparing a rehearsal with one run on the staging before the upgrade. The first upgrade with the mirror compares with prod's own results, which is why faked values that a mapping works out read as **Check**.
- Runtime settings, roles, members and service accounts, which differ between environments on purpose.
