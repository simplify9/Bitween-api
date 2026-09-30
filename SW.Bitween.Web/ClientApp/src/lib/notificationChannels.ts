import { useQuery } from "@tanstack/react-query";
import { api, type NotificationChannelUse } from "../api";
import { keys } from "../api/queryKeys";

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
      return `${use.name} — subscription notifications`;
    case "RetryPolicy":
      return `${use.name} — retry policy alert`;
    case "RetryGroup":
      return `${use.name} — retry group alert`;
    case "RetryAlertOverride":
      return `${use.name} — alert override`;
  }
};
