# Prod mirror

A staging Bitween can copy a prod Bitween's setup, with every value faked, so an upgrade can be tried on the staging before it reaches prod. On the staging, the list pages bring prod's items in and say how each one's test went, and each subscription's page compares it with prod's and tests it with prod's own traffic.

Prod is only read, through its own API, with a view-only login. Nothing is installed or changed on prod.

## What it needs

- **Prod on r10.0 or later.** A prod on r8, on Infolink (r6 and before), or on a build changed for one client is refused with the reason. Move such a client to r10 first.
- **A staging marked as a mirror.** Bringing adds prod's items to the staging, so it only runs on a Bitween whose deployment sets `Bitween__Mirror__Target=true`. A prod never sets it, so nothing can be brought onto a prod from its Settings page. Without it the pages still compare, but nothing is brought or tested.
- **A view-only login on prod**, one of:
  - a service account with the built-in **Viewer** role, and a token issued to it (**Administration, Service accounts** on prod). Releases that have service accounts take this;
  - on r10.0, which has none, the email and password of a member with the Viewer role.

Enter prod's address and the login in **Settings, Prod mirror**. The token and password are stored encrypted, which needs `Bitween:SettingsEncryptionKey`. The address is only an address, with no login, `?` or `#` in it; use `https`, and plain `http` only for an address inside the same cluster. Changing the address empties the saved token and password, so a login is never sent to an address it wasn't entered for: enter it again.

Reading prod's setup takes a request an item, and prod allows each login 600 requests a minute, so the staging keeps what it read for five minutes: the picker, bringing and the tests share one read, and a change made on prod shows on the staging within five minutes.

## What is copied, and how

Everything a configuration file can hold is copied by name: subscription categories, work groups, information types, partners, global values, connections, notification channels, retry policies, data sources and their SQL statements, subscriptions, API gateways, bus gateways and legacy notifiers. A kind prod's release doesn't have is left out and says so. The copy goes through the same engine as [`bitween apply`](cli.md).

Values are handled the moment they arrive, before anything is saved, so the staging never holds a real value of prod's:

| Kind of value | Examples | On the staging |
|---|---|---|
| How a flow behaves | formats, schedules, filters and match expressions, mapping rules, methods, delimiters, file name patterns, `{{globals.…}}` tokens | Copied as it is |
| Addresses | hosts, URLs, email addresses, connection strings, an API gateway's issuer, a connection's login URLs | Never copied: a placeholder that leads nowhere, `https://set-me.invalid` or `set-me@set-me.invalid`, until you fill in your test system's |
| Secrets | anything prod shows as `__private__`, values named like secrets (`apiKey`, `password`, `token`) | Never copied: left empty until you fill in staging's own. One a subscription's adapter can't be saved without is saved as `set-me` |
| Logins | a connection's client id and username; any setting, partner property or global value named like a user name or client id (`Username`, `SftpUser`, `ClientId`) | Never copied: `set-me` until you fill in your test login |
| Partner properties and global values | account numbers, codes, names | A fake of the same shape: digits stay digits, letters keep their case, dates stay dates in their format, GUIDs stay GUIDs, JSON keeps its keys |
| Partner API keys | | Keys of staging's own, the same on every copy |

Each address, secret and login left this way is something to fill in. Bringing lists them, and each subscription's **Against prod** section lists those it needs, with a link to the item on staging and one to the same item on prod, to look the value up. A value staging already has, one your team typed in, is kept and never reads as a difference from prod's. A placeholder is used rather than an empty value because Bitween won't save some items without one, such as an HTTP delivery without its URL. The same goes for a secret a subscription's adapter can't do without, such as a password: it is saved as `set-me`. Bitween hides a saved secret, so staging can't see when you put yours in: once you have, click **Filled in** on its row. Until then its tests don't run.

The same value always becomes the same fake, so a code named in two places still matches, and copying again changes nothing. A partner property or global value that is a setting rather than data, such as a time zone or a date format, can be kept as it is: list it in **Settings, Prod mirror, Values copied as they are**, one per line, as `partner:timeZone` or `global:formats/date`. Addresses and secrets are never kept.

Copied subscriptions arrive paused, and data sources and bus gateways switched off, so nothing on the staging runs on its own. On a mirror, a paused scheduled job or aggregation doesn't run on its schedule either, as it would elsewhere: **Receive now**, **Roll up now** and tests still run it.

Legacy setups are copied as prod has them: Internal and ApiCall subscriptions, legacy notifiers, and a response handed to an Internal or ApiCall subscription. Bitween keeps such a response link on an item that already has it but refuses a new one, and a copy is a new save. So the copy alone may recreate it: its calls carry a key it opens for the length of the copy, on a Bitween marked as a mirror. Someone editing on the staging never carries one and meets the same rule as on prod.

A field prod sends that this release doesn't know, from a newer or a customised build, can't be copied.

Prod may hold several items of a kind under one name. Each after the oldest is named apart on the staging: `test`, `test (2)`, `test (3)`. References to them still point at the right one.

## Bringing items from prod

On a mirror, the list pages of what runs flows offer **Bring from prod**: **All subscriptions**, **Scheduled jobs**, **Aggregations**, **Response subscriptions**, **API gateways** and **Bus gateways**. A page offers it only while prod has something of what it lists, and only to whoever may operate the mirror.

It lists prod's items of that kind, busiest first, each with its exchanges on prod over the last seven days, how many failed, when the last one ran, and whether the staging has it. The numbers come from the counts prod keeps by the hour. A prod whose release doesn't keep them shows none and says so, rather than read through its exchanges.

Tick the ones to bring, or all of them. **See what comes** then lists everything that comes with them, each with why:

- What an item names: a subscription's information type, partner, retry policy, data source, category and work group, the subscription it hands its responses to or rolls up, the notification channels it notifies and its alerts go to, and the global values sets and connections its adapters name in `{{globals.…}}` and `{{connections.…}}`.
- How prod's traffic reaches a subscription: the API gateway a partner calls it through, with that partner; the bus gateway that routes to it; the subscription whose responses it runs on. Each comes with what it names in turn.
- A data source's SQL statements.
- The adapters they run. A marketplace one is installed at prod's version; one only a person can add is named under **Needs you first**.

A picked gateway brings every subscription it leads to.

What the staging doesn't have comes as prod has it, handled as above, so subscriptions arrive paused and data sources and bus gateways switched off. The preview counts what comes by kind, saying how many you picked and how many they need, with each item behind **Show them**, lists the items you'll fill in on staging after bringing, one row each with its fields, and says which partners' and global values' data comes faked. What the staging has stays as it is, so edits made on the staging are never overwritten: a gateway the staging has only gets the partner or route that leads to what comes. Nothing is removed. An item that can't come yet, such as a subscription waiting for an adapter, stays behind and says why, and so does anything that needs it.

**Bring** then brings them. It carries on past an item that fails, and lists each failure with Bitween's reason.

## Against prod, on a subscription's page

On a mirror, every subscription's page ends with **Against prod**. A badge by its name says how it stands: **Same as prod**, **Differs from prod**, **Prod: to do**, or **Only on staging** when prod has no subscription of its name.

- **What differs** lists the subscription and every item it needs to run as on prod (the items bringing it would bring), each beside prod's: the same, the same with faked values, different field by field, or not on staging. The adapters they run come last. What is the same folds into one line.
- **Left to do** lists what only a person can do: each item with addresses, secrets or logins still to fill in, one row with its fields, **Fill it on staging** and **See it on prod**; an adapter to upload; and what staging doesn't have yet, with **Bring them from prod**, which opens on what comes. A notification channel's fields are listed too, but a test runs without them: a channel only sends alerts.
- **Test with prod's traffic**:
  - **Take prod's arrivals** takes up to five of the subscription's latest exchanges on prod, each with what came in and everything it led to, faked as samples are.
  - **Run the test** sends each arrival in on staging the way it came in on prod, and compares every exchange it leads to with its twin (see [How a test runs](#how-a-test-runs)). The page follows it as it runs. An arrival counts as **Same** when every exchange ends as prod's did and sends the same. It runs only once nothing it needs is left to fill in, and asks first when it uses an adapter that doesn't come with Bitween (see [Adapters that call out by themselves](#adapters-that-call-out-by-themselves)).
  - **Clear test data** removes what the test made on staging: each exchange its runs started, with its files, and each arrival's results. Prod's arrivals stay, to run again, and so does how the last run went, which the lists show.

Tests run one at a time across the staging. One started while another runs is refused until it finishes, except from a list, where the tests picked wait their turn.

A test calls a gateway as the partner, with the key of staging's own the copy gave it. Those keys come from the staging's `Token:Key`, so one made before that key changed no longer matches: the test sets it to the current one when it needs it. A key set on the staging is never touched, and a partner that has only those can't be called as.

## Samples of prod's traffic

A test takes a few of the subscription's recent exchanges on prod: up to five, mostly ones that worked, with one the partner answered badly and one that failed when prod has them, never a retry or one still running. Each comes with its **arrival**: the thing that came in on prod and every exchange it led to there. That is a partner's call through a gateway or the partner API, a document routed to the subscriptions that want it, a file a receiver read, or an aggregation, followed by the answers it handed on to response subscriptions and the messages bus gateways took. For each exchange it keeps what came in, what Bitween sent and what the partner answered, and how the exchange ended.

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

Prod is read gently: one subscription at a time, one page of its recent exchanges, then each file. Taking them again replaces the ones taken before.

## How a test runs

Running a test sends each arrival in on the staging the way it came in on prod, with prod's input as the staging keeps it, faked. The staging then runs everything after it by itself, as prod did:

| It came in on prod as | The test sends it in |
|---|---|
| A partner's call to an API gateway | As that partner, to the staging's gateway, with the key the copy gave the partner. A gateway that checks tokens from a login server isn't run: send that one in by hand, with a token from your test login server |
| A partner's call to the partner API | As that partner, to the staging's partner API |
| A document | To the staging, which routes it to the subscriptions that want it |
| A file a receiver read | Handed straight to the receiving subscription. Reading it from a server needs your test server, set in its place |
| An aggregation | **Aggregate now**, rolling up what the staging's tests ran of the subscription it aggregates, never the staging's own exchanges; when they ran none, the files prod rolled up, faked |
| An exchange started by hand | Started the same way |

The staging's subscriptions stay paused, and its data sources and bus gateways switched off, so nothing runs on its own. Only the test's traffic gets through: it carries a correlation id of its own, which every exchange it leads to carries on, across the bus too, and calls through a gateway carry a key the test opens for as long as it runs. That traffic passes paused subscriptions, goes through the bus gateways prod has on, and is never taken for a duplicate of the test before it. Anyone else's traffic meets the staging as it is.

When an exchange calls a partner, it calls the address your team filled in: your test system. What that system answers explains a verdict but never counts, since it isn't prod's partner. Arrivals run one at a time, and appear on the staging's **Exchanges** page under their correlation id.

Each of the staging's exchanges is paired with its twin on prod (the same subscription, in the order they started) and compared, and gets one of three verdicts:

- **Different**: it ended another way (worked, answered badly, failed), or what it sent changed its format, lost or gained a field, has a list of another length, or writes a value another way: a date in another format, a number turned text, a value left empty. An exchange one side ran and the other didn't is different too, and so is a call the staging's gateway turned away.
- **Check**: only values' text changed, and each kept its look. Fake data explains that when a mapping works values out, such as a sum or a cut; a wrong field taken doesn't, so it is worth a look.
- **Same**: nothing changed.

An arrival's verdict is the worst of its exchanges'. Both sides go through the same faker, so a value that is only faked reads the same on both. Opening an arrival shows how it came in, then its exchanges, the ones that differ first. Opening an exchange shows what changed first, prod beside staging, and folds what is the same into one line. A changed file opens field by field, and side by side. **What it used** compares the subscription, its information type, the partner it ran as, the global values it names and its adapters with prod's: prod's setup as it is today, faked as the copy fakes it, since prod's API doesn't give the setup an exchange ran with, only its adapters. These only explain a verdict, and so does what the partner's test system answered.

A test doesn't run while prod can't be read, since staging can't then check that what it needs is filled in. An arrival doesn't run when it can't be sent in the way it came in on prod, and the page says why: a subscription, partner, gateway or information type the staging doesn't have yet, or an input that isn't kept.

A run replaces the one before it.

## Adapters that call out by themselves

Bitween's own adapters call only what their settings say, and those are staging's own once filled in. An adapter that doesn't come with Bitween can also call addresses written in its own code, which staging can't see or change. So before a test runs a subscription that uses one, staging names the adapter and asks you to confirm it points at test systems, never at prod's partners. **Test from prod** on a list asks the same, with a tick, when any of those picked uses one.

## Adapters

Built-in adapters come with the release. One that prod uses and this release doesn't have is flagged, since it would stop working after the upgrade. Marketplace adapters are installed by the copy at the version prod runs. Simplify9's database and broker adapters (`bitween.db.…`, `bitween.bus.…`) are on the marketplace too; when prod doesn't say which version it runs, bringing an item that uses one asks for it to be installed from the **Marketplace** tab. Custom adapters can't be read from prod: bringing an item that uses one names it, with the version prod runs, to upload on the **Adapters** page, and so does the subscription's **Left to do**. Subscriptions that use one wait for it rather than fail.

## On the lists

On a mirror, the same list pages that offer **Bring from prod** show a **Prod** column: how each row's last test with prod's traffic went, **Same**, **Check** or **Different**; **No traffic** when prod had none of its exchanges to take; or **Not tested** when its arrivals haven't run since they were taken. A row prod has nothing of the name of shows `-`. A gateway's is that of the subscriptions it leads to that prod has too, the worst of those tested, with how many were when not all were. How a test went stays after its test data is cleared, and goes when prod's arrivals are taken again.

The **Prod** filter keeps the rows from prod, or only those whose test says one thing, such as **Not tested** or **Different from prod**. It pages like any other filter, however many items prod has.

**Test from prod** picks some of them, filtered by how their test went if need be, and tests them one after another, as each subscription's page would: prod's arrivals are taken first where none are, or none were found last time, then sent in. One with something left to fill in doesn't run, and its page says what. A gateway is tested through the subscriptions it leads to. While they run, the list says which one is under way and how many wait, with **Stop after this one** to drop those waiting.

When prod can't be read, the list says why above the rows, and the column is empty.

## Not covered yet

- Test servers inside Bitween (SFTP, FTP, mail) and staging answering calls in place of a partner. Until then, fill in your own test systems; a receiver's test starts from the file prod's receiver read.
- Databases: a flow that reads or writes one, and data sources. They will answer with what they returned on prod, with a note that a test database tests more.
- Files that can't be faked, such as PDFs: they will get stand-ins of the same kind, with a note asking for real test files.
- Comparing a test with one run on the staging before the upgrade. The first upgrade with the mirror compares with prod's own results, which is why faked values that a mapping works out read as **Check**.
- Runtime settings, roles, members and service accounts, which differ between environments on purpose.
- Marking a difference from prod as fine on staging, so it stops showing.
