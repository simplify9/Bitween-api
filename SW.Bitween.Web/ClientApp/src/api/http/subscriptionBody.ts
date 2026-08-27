import type { InlineSubscriptionDraft, Schedule } from "../types";
import { toRawMatchExpression } from "./matchExpression";

/**
 * The subscription wire shape, in the one place both the subscription endpoints and
 * the gateway endpoints can reach.
 *
 * It lives here rather than in `subscriptions.ts` because `gateways.ts` needs it too,
 * and `subscriptions.ts` already imports `gateways.ts` — putting it there would make
 * that cycle mutual.
 */

export interface RawKeyAndValue {
  key: string;
  value: string;
}

export interface RawSchedule {
  recurrence: Schedule["recurrence"];
  days: number;
  hours: number;
  minutes: number;
  backwards: boolean;
}

export const toKvArray = (record: Record<string, string>): RawKeyAndValue[] =>
  Object.entries(record).map(([key, value]) => ({ key, value }));

export const toRawSchedules = (schedules: Schedule[]): RawSchedule[] =>
  schedules.map((s) => ({
    recurrence: s.recurrence,
    days: s.days,
    hours: s.hours,
    minutes: s.minutes,
    backwards: s.backwards,
  }));

/**
 * A subscription defined on a gateway's canvas, in the shape the gateway endpoints
 * take. No `documentId`: a bus gateway imposes its own, and the API-gateway caller
 * adds the one its picker chose.
 */
export const inlineSubscriptionBody = (d: InlineSubscriptionDraft) => ({
  name: d.name.trim(),
  inactive: !d.enabled,
  workGroupId: d.workGroupId,
  retryPolicyId: d.retryPolicyId,
  customRetryPolicy: null,
  receiverId: d.receiverId,
  receiverProperties: toKvArray(d.receiverProperties),
  validatorId: d.validatorId,
  validatorProperties: toKvArray(d.validatorProperties),
  mapperId: d.mapperId,
  mapperProperties: toKvArray(d.mapperProperties),
  handlerId: d.handlerId,
  handlerProperties: toKvArray(d.handlerProperties),
  matchExpression: toRawMatchExpression(d.matchExpression),
  schedules: toRawSchedules(d.schedules),
  responseSubscriptionId: d.responseSubscriptionId,
  responseMessageTypeName: d.responseMessageTypeName,
});
