# Security

## Sign-in

Bitween supports three ways to sign in.

### Email and password

`POST /api/accounts/login` with `{ "Username": "...", "Password": "..." }`.

- Five wrong passwords in a row lock the account for 15 minutes. An administrator can unlock it early.
- Disabled accounts cannot sign in.
- Passwords need at least 8 characters, including an upper-case letter, a lower-case letter, a digit and a symbol. When an administrator sets someone else's password, only the 8-character minimum applies.
- Passwords are hashed with PBKDF2-HMAC-SHA256, 210,000 iterations and a random salt, and compared in constant time. Hashes stored before September 2026 use PBKDF2-SHA1 with 10,000 iterations. They still verify, and are only replaced when the password is next set.
- Bitween sends no email, so there is no self-service password reset.

Turning on the **Microsoft sign-in only** setting disables this method. New accounts are then created without a password.

### Microsoft

The sign-in page offers Microsoft once the Azure AD client id, tenant id and redirect URI settings are all set.

1. The browser signs in through an MSAL popup and gets an ID token.
2. It posts `{ "MsToken": "<id token>" }` to `POST /api/accounts/login`.
3. Bitween validates the token's signature and lifetime against Microsoft's published signing keys, reads the email, and signs in the Bitween account with that email.

Accounts are never created automatically. Add each person on the Team page first.

The redirect URI must be `/blank.html` on the Bitween host, such as `https://bitween.example.com/blank.html`, and must be registered on the Azure AD app. Other pages send a cross-origin opener policy that breaks the popup. Bitween logs a warning at startup when the redirect URI does not end in `/blank.html`.

Bitween does not check the token's issuer or audience. See [Known limitations](caveats.md#security).

### Break-glass login (removed)

`POST /api/login` signed in against `Bitween:AdminCredentials` and returned a token with every
permission, without touching the database and with no lockout. It shipped with a working default —
`admin:1234512345`, published in this repository — and neither the current nor the legacy UI ever
called it. A penetration test signed in with it. The endpoint and the setting are gone.

There is now no sign-in that bypasses the database. If the last account holding Administrator loses
the role, no request can restore it and the database has to be repaired directly.

## Tokens and sessions

- Sign-in returns `{ "jwt": "..." }`. Send it as `Authorization: Bearer <jwt>`.
- The JWT is signed with `Token:Key` and carries `Token:Issuer` and `Token:Audience`. It lasts for the **Sign-in session length** setting, 60 minutes by default.
- Sign-in also sets a `refresh_token` cookie that is `HttpOnly`, `Secure` and `SameSite=Lax`, valid for 30 days. Posting to the login endpoint with that cookie and no credentials returns a new JWT and replaces the refresh token.
- `POST /api/accounts/logout` deletes the refresh token and tells the browser to clear site data. The JWT stays valid until it expires.
- The admin UI signs out after 30 minutes without activity in any tab.
- Permissions are not in the token. They are read from the database on every request, so removing a role takes effect at once.

## Roles and permissions

A permission is written `area.action`. A member holds the union of their roles' permissions.

| Group | Area | Actions |
|---|---|---|
| Operate | `exchanges` | view, operate |
| Operate | `monitoring` | view |
| Operate | `dashboard` | view |
| Subscriptions | `subscriptions` | view, create, edit, delete, operate |
| Subscriptions | `partners` | view, create, edit, delete |
| Subscriptions | `documents` | view, create, edit, delete |
| Subscriptions | `global-values` | view, create, edit, delete |
| Subscriptions | `notifiers` | view, create, edit, delete |
| Subscriptions | `api-gateways` | view, create, edit, delete |
| Subscriptions | `bus-gateways` | view, create, edit, delete |
| Configuration | `data-sources` | view, create, edit, delete, operate |
| Configuration | `data-source-statements` | view, create, edit, delete |
| Configuration | `workgroups` | view, create, edit, delete |
| Configuration | `retry-policies` | view, create, edit, delete |
| Administration | `users` | view, create, edit, delete |
| Administration | `roles` | view, create, edit, delete |
| Administration | `settings` | view, edit |
| Administration | `audit` | view |

- `exchanges.operate` covers retrying and resubmitting exchanges, including running a scheduled retry now.
- `subscriptions.operate` covers pause, resume, receive now, roll up now and resetting a retry budget.
- `data-sources.operate` covers testing a connection. `data-source-statements` lets people write SQL for a database without the right to change its credentials.
- `documents` is the information types area.
- The audit trail has only `view`, because nothing can change it.

### Built-in roles

| Role | Permissions |
|---|---|
| Administrator | Every permission, including ones added in later versions |
| Member | Every action in the Operate, Subscriptions and Configuration groups |
| Viewer | Only the view actions in those groups |

Built-in roles cannot be edited or deleted. Custom roles can hold any combination, and cannot be deleted while members hold them.

Bitween refuses to remove, disable or demote the last enabled Administrator. Members cannot disable or remove themselves.

A request without the needed permission gets HTTP 401, the same status as a missing sign-in.

`GET /api/permissions` returns the full catalogue with labels and descriptions. `GET /api/accounts/profile` returns the signed-in member's permissions.

## Partners and API keys

Partners authenticate with an API key, sent in the key header (`partnerkey` unless renamed in Settings or on the gateway, and `partnerkey` is always accepted too), as `Authorization: Bearer <key>`, or as Basic auth with the key's name as username and the key as password. API gateways and the legacy exchange endpoints use it. An API gateway can take tokens from a login server instead. See [Entry points](entry-points.md#authentication).

- Generate a key on the partner page. It is shown once. Afterwards the API returns only its first five characters.
- Keys must be unique and are stored in plain text.
- Removing a key revokes it immediately.
- Basic auth's password is always the generated key, never one someone chose.

The seeded **SYSTEM** partner has a key named `default` whose value is fixed in the source code. That key can post documents of any information type to `POST /api/xchanges/{informationType}`, which feeds the filter. **Replace or remove the SYSTEM partner's key on every instance.**

## Secrets

- Adapter properties marked secret are masked as `__private__` in every API response and redacted from the audit trail. Their defaults are withheld from the adapter descriptions the UI reads. A published adapter that doesn't mark its passwords and keys as private has them returned in plain text, so check custom adapters before uploading them.
- The Rebex license key is the only secret setting. It is encrypted with AES-256-GCM, using a key derived from `Bitween:SettingsEncryptionKey`. Without that passphrase the value can only come from configuration, and changing the passphrase makes the stored value unreadable.
- Partner properties, global values, adapter properties and data source settings, including passwords, are stored unencrypted in the database.

## Exchange files

Exchange files are always written as private objects, so a storage URL doesn't open them. Files written by earlier versions keep the public access they were written with until the bucket deletes them.

- `GET /api/bitweendocs?documentKey=...`, which the UI uses to show file content, needs `exchanges.view`, and only reads a key that is exactly one of an exchange's files.
- Readers without a login use links Bitween serves at `/api/files/{seal}/{storage key}`: aggregation roll-ups, a partner's exchange result, the URLs in the exchange list. Anyone holding a link can read that one file, as with the public URL it replaces, until the bucket deletes the file. The seal can't be forged or moved to another file without `Token:Key`, and changing that key invalidates every link.
- File links have their own rate limit per client address, `Bitween:RateLimits:FileLinksPerMinute`, 60,000 by default, because an aggregation's handler downloads every link in a roll-up.
- On Azure, where the container decides privacy for every file in it, Bitween switches its container to private at startup, keeping its stored access policies. Storage URLs handed out before that stop opening.
- The Settings page checks, every few minutes, whether a file Bitween writes opens straight from the bucket without credentials, and warns when it does, on every provider.
- A roll-up made before Bitween served its own links lists storage URLs. Retrying it swaps each URL of an exchange file for a Bitween link, so the handler still gets every file.

## Data sources

Data sources open connections to customers' brokers and databases, so they only run on nodes with `Bitween__BusProvidersEnabled` set.

- Their settings are masked in responses but stored unencrypted.
- Anyone with `data-sources.view` can browse a database's catalogue and see the login's privileges.
- Saving or testing an Oracle statement runs `DBMS_SQL.PARSE`, which executes DDL. Connect Oracle sources with a login that has no DDL rights.
- SQL only comes from named statements, and every value is bound as a parameter, unless a data source turns on `AllowAdHocSql`.

## HTTP hardening

Every response carries these headers.

| Header | Value |
|---|---|
| `X-Frame-Options` | `DENY` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `X-Permitted-Cross-Domain-Policies` | `none` |
| `Cross-Origin-Opener-Policy` | `same-origin-allow-popups`, or `unsafe-none` on `/blank.html` so the Microsoft popup works |
| `Content-Security-Policy` | Scripts from the same origin only, inline styles allowed, Microsoft sign-in allowed for connections, frames and forms, and no framing by other sites. Not sent under `/swagger`. |

- JSON responses carry `Cache-Control: no-store`. Hashed UI assets are cached for a year, and `index.html` is always revalidated.
- CORS is closed unless `Bitween:CorsOrigins` lists origins, which are then allowed with credentials.
- Request bodies are limited to 50 MB.
- Responses are gzip-compressed, including over HTTPS.

## Audit trail

Bitween records every change to configuration: subscriptions and their schedules, categories, partners and API keys, information types, gateways and routes, work groups, retry policies and alert overrides, notifiers, global value sets, settings, accounts, roles and role assignments.

Each entry holds the time, the member, the entity and its key, whether it was added, modified or deleted, and the old and new value of each changed property. Entries from one save share a correlation id. Adapter properties, partner properties, global values, API key values, passwords and secret settings are redacted. Exchanges and other runtime records are not audited. Reading an adapter's source is: each file read is an `AdapterSourceAccess` entry with the adapter, version and file.

The trail is written in the same transaction as the change, and no API edits or deletes it. Browse it on the Audit trail page or with `GET /api/audit`.

## Production checklist

1. Set `Bitween__InitialAdminPassword` when installing; a new installation refuses to start
   without it ([details](configuration.md#bitweeninitialadminpassword-on-a-new-installation)). On an
   existing one still using the published default password, set it too, or change that password
   yourself: until then, whoever signs in with it first chooses the new one.
3. Replace or remove the SYSTEM partner's API key.
4. Set `Token__Key` to a long random secret, and choose your own `Token__Issuer` and `Token__Audience`.
5. Set `Bitween__SettingsEncryptionKey` before saving a Rebex license key.
6. Give the chart's Ingress a TLS entry for its host, so file links are built on an https address, and treat roll-ups and exchange results as holding the files themselves: a link opens its file without a login. Over plain http a link and its file can be read on the way.
7. Serve Bitween over HTTPS. The refresh cookie is always marked `Secure`.
8. Connect data sources with least-privilege logins, and never with DDL rights on Oracle.
9. Review [Known limitations](caveats.md#security).
