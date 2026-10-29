/**
 * How an adapter's settings are named and arranged on screen.
 *
 * An adapter declares its settings by the key it reads them under, which is a C# property name or
 * whatever its manifest author typed: "ApiKey", "LoginTokenPath", "TimeoutSeconds". Those keys are
 * what is stored and what operators and docs refer to, so they never change; this only decides the
 * label a person reads and which collapsible section an optional setting sits in. Every adapter
 * goes through the same rules - built in, published or installed from the marketplace - because
 * none of them is special here.
 */

/** Words that read wrong once a key is split and lower-cased. Looked up lower-case. */
const WORDS: Record<string, string> = {
  api: "API",
  url: "URL",
  uri: "URI",
  id: "ID",
  ids: "IDs",
  http: "HTTP",
  https: "HTTPS",
  json: "JSON",
  xml: "XML",
  csv: "CSV",
  sql: "SQL",
  ssl: "SSL",
  tls: "TLS",
  ssh: "SSH",
  ftp: "FTP",
  ftps: "FTPS",
  sftp: "SFTP",
  smtp: "SMTP",
  pop3: "POP3",
  imap: "IMAP",
  oauth: "OAuth",
  jwt: "JWT",
  ttl: "TTL",
  tcp: "TCP",
  ip: "IP",
  dns: "DNS",
  s3: "S3",
  aws: "AWS",
  sas: "SAS",
  utc: "UTC",
  db: "DB",
  ms: "ms",
};

/** A trailing unit word becomes a parenthesised unit: "TimeoutSeconds" reads "Timeout (seconds)". */
const UNITS: Record<string, string> = {
  seconds: "seconds",
  secs: "seconds",
  ms: "ms",
  millis: "ms",
  milliseconds: "ms",
  minutes: "minutes",
  mins: "minutes",
  hours: "hours",
  days: "days",
  bytes: "bytes",
  kb: "KB",
  mb: "MB",
};

/**
 * A setting's key as a label: "ApiKey" reads "API key", "UserName" and "Username" read "User name",
 * "TimeoutSeconds" reads "Timeout (seconds)", "client_id" reads "Client ID". A display name the
 * adapter declares wins over all of this - it was written by someone who knew what the setting is.
 */
export function propertyLabel(key: string, displayName?: string | null): string {
  if (displayName && displayName.trim()) return displayName.trim();
  const words = key
    // "Username" is one word in the key but two on screen, like its PascalCase spelling.
    .replace(/username/gi, (m) => `${m.slice(0, 4)} ${m.slice(4)}`)
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .split(/[\s_.-]+/)
    .filter(Boolean);
  if (words.length === 0) return key;

  let unit: string | null = null;
  if (words.length > 1) {
    unit = UNITS[words[words.length - 1].toLowerCase()] ?? null;
    if (unit) words.pop();
  }

  const label = words
    .map((word, i) => {
      const known = WORDS[word.toLowerCase()];
      if (known) return known;
      // "Oauth2" and the like: the dictionary word with its version number kept.
      const versioned = /^([a-z]+)(\d+)$/i.exec(word);
      if (versioned && WORDS[versioned[1].toLowerCase()]) return WORDS[versioned[1].toLowerCase()] + versioned[2];
      // An acronym the dictionary doesn't know ("SMB", "EDI") is still an acronym.
      if (word.length > 1 && word === word.toUpperCase() && /[A-Z]/.test(word)) return word;
      return i === 0 ? word[0].toUpperCase() + word.slice(1).toLowerCase() : word.toLowerCase();
    })
    .join(" ");
  return unit ? `${label} (${unit})` : label;
}

export const AUTHENTICATION = "Authentication";
export const CONNECTION = "Connection";
export const REQUEST = "Request";
export const ADVANCED = "Advanced";

/** The order the guessed sections appear in: what most people change first, first. */
const HEURISTIC_ORDER = [REQUEST, CONNECTION, AUTHENTICATION, ADVANCED];

/**
 * Which section an optional setting belongs to when its adapter doesn't say.
 *
 * A guess from the key alone, so it errs towards "Advanced": a setting that lands there is still
 * one click away, and one put in the wrong named section misleads. The secret flag is deliberately
 * not used - an HTTP handler's Headers are secret because they carry keys, but they are part of
 * the request.
 */
export function guessGroup(key: string): string {
  const k = key.toLowerCase().replace(/[\s_.-]/g, "");
  if (
    /^(auth|login|client|token|password|passwd|secret|apikey|user|oauth|bearer|credential|cert|privatekey|passphrase|tenant|scope|accesskey|signing)/.test(k) ||
    /(password|secret|apikey|token|credential|passphrase|privatekey|accesskey)$/.test(k)
  )
    return AUTHENTICATION;
  if (/^(host|port|server|database|schema|bucket|container|queue|topic|exchange|vhost|virtualhost|folder|directory|path|region|account|instance|domain|mailbox)/.test(k))
    return CONNECTION;
  if (
    /^(verb|method|headers?|contenttype|accept|body|request|defaultrequest|query|payload|charset|encoding|url|uri|endpoint)/.test(k) ||
    /(url|uri|endpoint|body|request|headers?|contenttype)$/.test(k)
  )
    return REQUEST;
  return ADVANCED;
}

export interface Grouped<T> {
  name: string;
  items: T[];
}

/**
 * Optional settings arranged into sections. A group the adapter declares is used as it is, in the
 * order its settings first appear; anything without one is guessed into the fixed sections after
 * them. A section exists only if something is in it.
 */
export function groupSettings<T extends { key: string; group?: string | null }>(props: T[]): Grouped<T>[] {
  const declared = new Map<string, T[]>();
  const guessed = new Map<string, T[]>();
  for (const p of props) {
    const own = p.group?.trim();
    const into = own ? declared : guessed;
    const name = own || guessGroup(p.key);
    into.set(name, [...(into.get(name) ?? []), p]);
  }
  const result: Grouped<T>[] = [...declared].map(([name, items]) => ({ name, items }));
  for (const name of HEURISTIC_ORDER) {
    const items = guessed.get(name);
    if (!items) continue;
    // A declared "Advanced" and a guessed one are the same section to whoever reads it.
    const same = result.find((g) => g.name.toLowerCase() === name.toLowerCase());
    if (same) same.items.push(...items);
    else result.push({ name, items });
  }
  return result;
}

/** A value short enough to stand for itself in a collapsed section's header. */
const SHOWN_VALUE_LENGTH = 24;

/**
 * What a collapsed section says about itself: the values set, when there are few and they are
 * short ("post, application/json"), otherwise how many are set. A secret is named, never shown.
 */
export function sectionSummary(items: { label: string; value: string; secret: boolean }[]): string {
  const set = items.filter((i) => i.value !== "");
  if (set.length === 0) return "none set";
  if (set.length > 3) return `${set.length} of ${items.length} set`;
  const parts = set.map((i) =>
    i.secret || i.value.length > SHOWN_VALUE_LENGTH || /\n/.test(i.value) ? `${i.label} set` : i.value,
  );
  return parts.join(", ");
}

/** Help text longer than this is clamped to two lines with a "more" to open it. */
export const LONG_HELP = 140;
export const isLongHelp = (text: string) => text.length > LONG_HELP || text.split("\n").length > 2;
