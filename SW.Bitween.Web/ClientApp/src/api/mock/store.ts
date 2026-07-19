import type {
  GlobalValuesSet,
  InformationType,
  Integration,
  Invite,
  NotificationEntry,
  Notifier,
  Partner,
  RetryPolicy,
  Setting,
  TrailEntry,
  User,
  WorkGroup,
} from "../types";
import { DEMO_PASSWORD, SEED_INVITES, SEED_ROLES, SEED_USERS, type SeedRole } from "./seed";
import {
  SEED_API_GATEWAYS,
  SEED_BUS_GATEWAYS,
  SEED_CREDENTIALS,
  SEED_INFORMATION_TYPES,
  SEED_INTEGRATIONS,
  SEED_INTEGRATION_TRAILS,
  SEED_NOTIFIERS,
  SEED_PARTNERS,
  SEED_RETRY_POLICIES,
  SEED_SETTINGS,
  SEED_TRAILS,
  SEED_VALUE_SETS,
  SEED_WORK_GROUPS,
  seedExchanges,
  seedNotifications,
  type SeedExchange,
  type StoredApiGateway,
  type StoredBusGateway,
  type StoredCredential,
} from "./seedConfig";

/**
 * A tiny localStorage-backed "database" so demo changes (invites, new
 * roles, disabled members) survive reloads. `reset()` restores the seeds.
 */
export interface MockDb {
  users: User[];
  roles: SeedRole[];
  invites: Invite[];
  /** userId -> password (plaintext; prototype only). */
  passwords: Record<string, string>;
  /** reset token -> userId */
  resetTokens: Record<string, string>;
  sessionUserId: string | null;
  // — configuration entities —
  partners: Partner[];
  credentials: StoredCredential[];
  informationTypes: InformationType[];
  trails: Record<number, TrailEntry[]>;
  valueSets: GlobalValuesSet[];
  integrations: Integration[];
  integrationTrails: Record<number, TrailEntry[]>;
  workGroups: WorkGroup[];
  apiGateways: StoredApiGateway[];
  busGateways: StoredBusGateway[];
  exchanges: SeedExchange[];
  retryPolicies: RetryPolicy[];
  notifiers: Notifier[];
  notifications: (NotificationEntry & { notifierId: number })[];
  settings: Setting[];
}

const KEY = "bitween-proto-db-v9";

const seedDb = (): MockDb => ({
  users: structuredClone(SEED_USERS),
  roles: structuredClone(SEED_ROLES),
  invites: structuredClone(SEED_INVITES),
  passwords: Object.fromEntries(
    SEED_USERS.filter((u) => u.status !== "invited").map((u) => [u.id, DEMO_PASSWORD]),
  ),
  resetTokens: {},
  sessionUserId: null,
  partners: structuredClone(SEED_PARTNERS),
  credentials: structuredClone(SEED_CREDENTIALS),
  informationTypes: structuredClone(SEED_INFORMATION_TYPES),
  trails: structuredClone(SEED_TRAILS),
  valueSets: structuredClone(SEED_VALUE_SETS),
  integrations: structuredClone(SEED_INTEGRATIONS),
  integrationTrails: structuredClone(SEED_INTEGRATION_TRAILS),
  workGroups: structuredClone(SEED_WORK_GROUPS),
  apiGateways: structuredClone(SEED_API_GATEWAYS),
  busGateways: structuredClone(SEED_BUS_GATEWAYS),
  exchanges: seedExchanges(),
  retryPolicies: structuredClone(SEED_RETRY_POLICIES),
  notifiers: structuredClone(SEED_NOTIFIERS),
  notifications: seedNotifications(),
  settings: structuredClone(SEED_SETTINGS),
});

export const loadDb = (): MockDb => {
  try {
    const raw = localStorage.getItem(KEY);
    if (raw) return JSON.parse(raw) as MockDb;
  } catch {
    // corrupted storage — fall through to a fresh seed
  }
  const db = seedDb();
  saveDb(db);
  return db;
};

export const saveDb = (db: MockDb) => {
  localStorage.setItem(KEY, JSON.stringify(db));
};

export const resetDb = (): MockDb => {
  const current = loadDb();
  const db = seedDb();
  // keep the signed-in persona when it exists in the seeds
  if (current.sessionUserId && db.users.some((u) => u.id === current.sessionUserId)) {
    db.sessionUserId = current.sessionUserId;
  }
  saveDb(db);
  return db;
};

/** Simulated network latency so loading states are honest. */
export const delay = () =>
  new Promise<void>((resolve) => setTimeout(resolve, 120 + Math.random() * 200));
