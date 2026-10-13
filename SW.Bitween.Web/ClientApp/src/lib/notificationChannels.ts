import { useQuery } from "@tanstack/react-query";
import { Hash, Mail, MessagesSquare, type LucideIcon } from "lucide-react";
import { api, type NotificationChannelUse, type SubscriptionNotification } from "../api";
import { keys } from "../api/queryKeys";

/**
 * The channel types offered first, each its own built-in adapter. Bitween writes their message -
 * what happened, the error, a link back - so setting one up is only saying where it goes. Any other
 * handler adapter can still be a channel, behind "Use another adapter".
 */
export interface ChannelType {
  adapterId: string;
  label: string;
  /** What setting it up asks for, under the label on its card. */
  needs: string;
  icon: LucideIcon;
}

export const CHANNEL_TYPES: readonly ChannelType[] = [
  {
    adapterId: "NativeEmailMessageHandler",
    label: "Email",
    needs: "Who to send to. The mail server is set once, in Settings.",
    icon: Mail,
  },
  {
    adapterId: "NativeTeamsMessageHandler",
    label: "Microsoft Teams",
    needs: "A Teams Workflows webhook for the channel.",
    icon: MessagesSquare,
  },
  {
    adapterId: "NativeSlackMessageHandler",
    label: "Slack",
    needs: "An incoming webhook for the channel.",
    icon: Hash,
  },
];

/** The main type a channel's adapter is, or null for any other adapter. */
export const channelTypeOf = (adapterId: string | null | undefined): ChannelType | null =>
  CHANNEL_TYPES.find((t) => t.adapterId === adapterId) ?? null;

/** Where to go for a use of a channel: the subscription, or the retry policy. */
export const channelUseHref = (use: NotificationChannelUse): string | null =>
  use.subscriptionId !== null
    ? `/subscriptions/${use.subscriptionId}`
    : use.retryPolicyId !== null
      ? `/retry-policies/${use.retryPolicyId}`
      : null;

/** Channel names by id, for showing a picked channel anywhere without its picker. */
export function useNotificationChannelNames() {
  return useQuery({
    queryKey: keys.notificationChannels.lookup,
    queryFn: () => api.lookupNotificationChannels(),
  });
}

/** How one use of a channel reads in a list: which place it is, and what kind. */
export const describeChannelUse = (use: NotificationChannelUse): string => {
  switch (use.kind) {
    case "Subscription":
      return `${use.name} - subscription notifications`;
    case "RetryPolicy":
      return `${use.name} - retry policy alert`;
    case "RetryGroup":
      return `${use.name} - retry group alert`;
    case "RetryAlertOverride":
      return `${use.name} - alert override`;
  }
};

/** How a notifications row names itself when it leaves for a new channel. */
export const notificationSlot = (index: number) => `notifications:${index}`;

/**
 * The notifications with a channel just made from row `slot` filled in - or added as a new row
 * sending on failures, when the row is gone or the slot is someone else's.
 */
export function withPickedChannel(
  notifications: SubscriptionNotification[],
  slot: string | null,
  channelId: number,
): SubscriptionNotification[] {
  const index = slot?.startsWith("notifications:") ? Number(slot.slice("notifications:".length)) : -1;
  if (index >= 0 && index < notifications.length)
    return notifications.map((n, i) => (i === index ? { ...n, channelId } : n));
  return [...notifications, { channelId, onFailure: true, onBadResult: false, onSuccess: false, onQuiet: false }];
}

/** Whether any notification row is still missing its channel or every outcome - the save would be refused. */
export const notificationsIncomplete = (notifications: SubscriptionNotification[]): boolean =>
  notifications.some((n) => n.channelId <= 0 || (!n.onFailure && !n.onBadResult && !n.onSuccess && !n.onQuiet));
