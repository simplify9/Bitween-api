import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, type PortalProperty } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { formatDateTime } from "../../lib/dates";

/**
 * Their own partner properties, only those we show them, such as where we deliver to them. A secret
 * one shows without its value; they may replace it if we let them. A change waits for our approval
 * when their changes need it.
 */
export function YourDetails() {
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("portal-properties.edit");
  const details = useQuery({ queryKey: keys.portal.properties, queryFn: () => api.getPortalProperties() });
  const [editing, setEditing] = useState(false);
  // A change sent for approval says so in the waiting notice instead.
  const [saved, setSaved] = useState(false);
  const withdraw = useMutation({
    mutationFn: () => api.withdrawPortalProperties(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: keys.portal.properties }),
  });

  if (details.isPending)
    return (
      <Panel title="Your details">
        <LoadingBlock />
      </Panel>
    );
  if (details.isError)
    return (
      <Panel title="Your details">
        <LoadError error={details.error} what="your details" onRetry={() => void details.refetch()} />
      </Panel>
    );

  const d = details.data;
  const editable = d.properties.filter((p) => p.editable);
  return (
    <Panel
      title="Your details"
      description="What we hold for your company, such as where we deliver to you."
      action={
        canEdit &&
        editable.length > 0 && (
          <Button size="sm" onClick={() => setEditing(true)} title="Change the details you're allowed to change">
            Change
          </Button>
        )
      }
    >
      {d.properties.length === 0 ? (
        <p className="text-sm text-ink-500">Nothing to show yet.</p>
      ) : (
        <dl className="space-y-1.5 text-sm">
          {d.properties.map((p) => (
            <div key={p.key} className="flex items-baseline justify-between gap-3">
              <dt className="shrink-0 font-mono text-xs text-ink-600">{p.key}</dt>
              <dd className="min-w-0 truncate text-right text-ink-900" title={p.secret ? "Kept secret. You can replace it, not read it." : (p.value ?? "")}>
                {p.secret ? <Badge title="Kept secret. You can replace it, not read it.">Secret</Badge> : p.value || "-"}
              </dd>
            </div>
          ))}
        </dl>
      )}

      {d.waiting ? (
        <div className="mt-3 flex flex-wrap items-center gap-2 rounded-lg bg-warn-100 px-3 py-2 text-[13px] text-ink-700">
          <span className="min-w-0 flex-1">
            Your change to {Object.keys(d.waiting.values).join(", ")} from {formatDateTime(d.waiting.requestedOn)} waits for
            our approval.
          </span>
          {canEdit && (
            <Button size="sm" busy={withdraw.isPending} onClick={() => withdraw.mutate()} title="Take your change back before we decide on it">
              Take it back
            </Button>
          )}
        </div>
      ) : (
        d.rejected && (
          <p className="mt-3 rounded-lg bg-ink-100 px-3 py-2 text-[13px] text-ink-700">
            We didn't approve your last change ({formatDateTime(d.rejected.decidedOn)})
            {d.rejected.note ? `: ${d.rejected.note}` : "."}
          </p>
        )
      )}
      {saved && !d.waiting && <p className="mt-2 text-[13px] text-ok-700">Saved.</p>}
      <FormError>{withdraw.error?.message}</FormError>

      {editing && (
        <ChangeDetailsDialog
          properties={editable}
          pending={d.waiting?.values ?? {}}
          needsApproval={d.needsApproval}
          onClose={() => setEditing(false)}
          onSaved={(waiting) => {
            setEditing(false);
            setSaved(!waiting);
            void queryClient.invalidateQueries({ queryKey: keys.portal.properties });
          }}
        />
      )}
    </Panel>
  );
}

function ChangeDetailsDialog({
  properties,
  pending,
  needsApproval,
  onClose,
  onSaved,
}: {
  properties: PortalProperty[];
  /** Their change still waiting, to carry on from. */
  pending: Record<string, string | null>;
  needsApproval: boolean;
  onClose: () => void;
  onSaved: (waiting: boolean) => void;
}) {
  // A secret starts empty, meaning keep it; anything else starts from their waiting change, or what it is.
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(properties.map((p) => [p.key, p.secret ? "" : (pending[p.key] ?? p.value ?? "")])),
  );
  const save = useMutation({
    mutationFn: () => api.savePortalProperties(values),
    onSuccess: (result) => onSaved(result.waiting),
  });

  return (
    <Dialog title="Change your details" onClose={onClose}>
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault();
          save.mutate();
        }}
      >
        {properties.map((p) => (
          <Field key={p.key} label={p.key} hint={p.secret ? "Secret. Leave it empty to keep it as it is." : undefined}>
            <TextInput
              type={p.secret ? "password" : "text"}
              autoComplete="off"
              value={values[p.key] ?? ""}
              maxLength={4000}
              onChange={(e) => setValues((v) => ({ ...v, [p.key]: e.target.value }))}
            />
          </Field>
        ))}
        {needsApproval && (
          <p className="text-[13px] text-ink-600">Your changes wait for our approval before they're used.</p>
        )}
        <FormError>{save.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={save.isPending}>
            {needsApproval ? "Send for approval" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
