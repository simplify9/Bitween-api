import { useQuery } from "@tanstack/react-query";
import { TriangleAlert } from "lucide-react";
import { api, type Schedule } from "../../api";
import { keys } from "../../api/queryKeys";

/**
 * Under an aggregation's schedule: whether retention removes exchanges before the aggregation rolls
 * them up. Nothing holds them back for it, so this is where it's said, as the schedule is edited.
 */
export function AggregationRetentionWarning({ schedules }: { schedules: Schedule[] }) {
  const { data } = useQuery({
    queryKey: keys.settings.aggregationRetention(JSON.stringify(schedules)),
    queryFn: () => api.checkAggregationRetention(schedules),
    enabled: schedules.length > 0,
  });

  if (!schedules.length || !data?.warning) return null;
  return (
    <p
      className="mt-3 flex items-start gap-2 rounded-lg bg-warn-100/50 px-3 py-2 text-[13px] text-warn-800"
      title="Retention removes exchanges older than the days set under Settings → Documents & storage, whether or not an aggregation has rolled them up."
    >
      <TriangleAlert className="mt-0.5 size-3.5 shrink-0" aria-label="Warning" />
      <span>{data.warning}</span>
    </p>
  );
}
