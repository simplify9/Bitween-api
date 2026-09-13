import { useState } from "react";
import { Link } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { api } from "../../../api";
import { useSessionCan } from "../../../auth/guards";
import { SearchSelect } from "../../../components/ui/SearchSelect";
import { WorkGroupDialog } from "../../../components/config/WorkGroupDialog";
import { keys } from "../../../api/queryKeys";
import { Fact } from "./Fact";

/**
 * Which lane a subscription runs in, and what happens when it fails.
 *
 * Extracted from `Overview` so the create pages can offer the same two settings the
 * edit page does. They belong to no pipeline stage — they are properties of the
 * subscription itself — which is why they sit beside the facts rather than in the rail.
 *
 * Both were create-time blind spots: the API has always accepted them on a create, but
 * no create page asked, so every new subscription was born ungrouped and un-retried and
 * had to be reopened to fix that.
 */
export function LaneAndRetry({
  workGroupId,
  retryPolicyId,
  onWorkGroupChange,
  onRetryPolicyChange,
  canEdit,
  idPrefix,
}: {
  workGroupId: number | null;
  retryPolicyId: number | null;
  onWorkGroupChange: (id: number | null) => void;
  onRetryPolicyChange: (id: number | null) => void;
  canEdit: boolean;
  /** Ids have to be unique per page — the create pages render this beside their own fields. */
  idPrefix: string;
}) {
  const canCreateWorkGroup = useSessionCan("workgroups.create");
  const workGroups = useQuery({ queryKey: keys.workGroups.list, queryFn: () => api.listWorkGroups() });
  const retryPolicies = useQuery({
    queryKey: keys.retryPolicies.list,
    queryFn: () => api.listRetryPolicies(),
  });
  /** undefined = closed, null = creating, number = editing that group. */
  const [groupDialog, setGroupDialog] = useState<number | null | undefined>(undefined);

  return (
    <>
      <Fact label="Work group">
        <div className="w-52">
          <SearchSelect
            id={`${idPrefix}-wg`}
            value={workGroupId === null ? "" : String(workGroupId)}
            disabled={!canEdit}
            onChange={(v) => onWorkGroupChange(v === "" ? null : Number(v))}
            clearLabel="Ungrouped (default lane)"
            options={(workGroups.data ?? []).map((w) => ({ value: String(w.id), label: w.name }))}
          />
        </div>
        <div className="mt-1 flex items-center gap-3">
          {workGroupId !== null && (
            <button
              type="button"
              onClick={() => setGroupDialog(workGroupId)}
              className="text-[12px] font-medium text-crimson-700 hover:underline"
            >
              Edit it
            </button>
          )}
          {canCreateWorkGroup && (
            <button
              type="button"
              onClick={() => setGroupDialog(null)}
              className="text-[12px] font-medium text-crimson-700 hover:underline"
            >
              + New
            </button>
          )}
        </div>
      </Fact>
      <Fact label="Retry policy">
        <div className="w-52">
          <SearchSelect
            id={`${idPrefix}-rp`}
            value={retryPolicyId === null ? "" : String(retryPolicyId)}
            disabled={!canEdit}
            onChange={(v) => onRetryPolicyChange(v === "" ? null : Number(v))}
            clearLabel="None — failures are not retried"
            options={(retryPolicies.data ?? []).map((p) => ({ value: String(p.id), label: p.name }))}
          />
        </div>
        {retryPolicyId !== null && (
          <Link
            to={`/retry-policies/${retryPolicyId}`}
            className="mt-1 inline-block text-[12px] font-medium text-crimson-700 hover:underline"
          >
            View
          </Link>
        )}
      </Fact>
      {groupDialog !== undefined && (
        <WorkGroupDialog
          groupId={groupDialog}
          onClose={() => setGroupDialog(undefined)}
          onSaved={onWorkGroupChange}
        />
      )}
    </>
  );
}
