import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { api, referencesPartnerProp, type ApiCredentialRef, type Partner } from "../../api";
import { Can } from "../../auth/guards";
import { Button, FormError } from "../ui/basics";
import { CopyField } from "../ui/CopyField";
import { Field, TextInput } from "../ui/forms";
import { KeyValueEditor, toRecord, toRows, type KvRow } from "../ui/KeyValueEditor";
import { ConfirmDialog, Dialog } from "../ui/overlays";
import { Panel } from "../ui/Panel";
import { MiniTable } from "../ui/Table";
import { SubscriptionMiniList, usePartnerSubscriptions } from "./shared";

/**
 * Everything about a partner that can be *edited*, as one component.
 *
 * Used by the partner's own page and by the dialog that pickers open, so the two
 * cannot drift: a field added here appears in both, and a rule about what a
 * property means is written once. What is deliberately not here is the partner's
 * read-only context — used-by, recent exchanges — which only the page has room
 * for and which nobody edits.
 *
 * Controlled: the draft and its changes belong to the host, because the two hosts
 * save differently (a page-wide unsaved bar versus a dialog's own button). API
 * keys are the exception and act immediately — issuing a key is not a draft, and
 * the secret is shown exactly once.
 */
export interface PartnerDraft {
  name: string;
  properties: KvRow[];
}

export const partnerDraftOf = (p: Pick<Partner, "name" | "adapterProperties">): PartnerDraft => ({
  name: p.name,
  properties: toRows(p.adapterProperties),
});

export const partnerDirty = (draft: PartnerDraft, saved: PartnerDraft): boolean =>
  draft.name !== saved.name ||
  JSON.stringify(toRecord(draft.properties)) !== JSON.stringify(toRecord(saved.properties));

/** What the host sends to `updatePartner`. */
export const partnerChanges = (draft: PartnerDraft) => ({
  name: draft.name.trim(),
  adapterProperties: toRecord(draft.properties.filter((r) => r.key.trim())),
});

export function PartnerFields({
  draft,
  onChange,
  canEdit,
  isSystem = false,
  /** null until the partner exists — a key can only be issued against a real one. */
  partnerId,
  credentials,
  /** The page edits the name in its own title; a dialog has to ask for it. */
  showName = false,
}: {
  draft: PartnerDraft;
  onChange: (draft: PartnerDraft) => void;
  canEdit: boolean;
  isSystem?: boolean;
  partnerId: number | null;
  credentials: ApiCredentialRef[];
  showName?: boolean;
}) {
  const queryClient = useQueryClient();
  const partnerSubscriptions = usePartnerSubscriptions();
  const [addingKey, setAddingKey] = useState(false);
  const [revoking, setRevoking] = useState<string | null>(null);

  const revoke = useMutation({
    mutationFn: (keyName: string) => api.revokePartnerCredential(partnerId!, keyName),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["partner", partnerId] });
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
    },
  });

  const users = partnerId === null ? [] : (partnerSubscriptions.get(partnerId) ?? []);

  return (
    <div className="space-y-5">
      {showName && (
        <div className="w-80">
          <Field label="Name" htmlFor="pf-name">
            <TextInput
              id="pf-name"
              value={draft.name}
              disabled={!canEdit || isSystem}
              placeholder="e.g. Northwind Foods"
              onChange={(e) => onChange({ ...draft, name: e.target.value })}
            />
          </Field>
        </div>
      )}

      <Panel
        title="Properties"
        description="Key-value settings adapters can reference — use the reference token inside any adapter field."
      >
        <KeyValueEditor
          rows={draft.properties}
          onChange={(properties) => onChange({ ...draft, properties })}
          keyLabel="Property"
          valueLabel="Value"
          keyPlaceholder="storeId"
          valuePlaceholder="CR-114"
          editable={canEdit}
          token={(row) => `{{partner.${row.key.trim()}}}`}
          emptyText="No properties yet."
          rowDetails={(row) => {
            if (!row.key.trim() || partnerId === null) return null;
            return (
              <SubscriptionMiniList
                items={users.filter((s) => referencesPartnerProp(s, row.key.trim()))}
                emptyText="Not referenced by any subscription — safe to change or remove."
              />
            );
          }}
        />
      </Panel>

      <Panel
        title="API keys"
        description="Credentials partners use to call Bitween's gateways."
        action={
          partnerId !== null && (
            <Can permission="partners.edit">
              <Button size="sm" onClick={() => setAddingKey(true)}>
                <Plus className="size-3.5" /> New key
              </Button>
            </Can>
          )
        }
      >
        {partnerId === null ? (
          <p className="text-[13px] text-ink-500">
            A key can only be issued to a partner that exists. Create this one and its keys section
            opens right here — you won't be sent anywhere.
          </p>
        ) : (
          <>
            <MiniTable
              rows={credentials}
              rowKey={(c) => c.name}
              empty="No API keys yet."
              columns={[
                {
                  header: "Key",
                  truncate: true,
                  cell: (c) => <span className="block truncate font-medium text-ink-800">{c.name}</span>,
                },
                {
                  header: "Prefix",
                  cell: (c) => <code className="font-mono text-xs text-ink-500">{c.keyPrefix}…</code>,
                },
                {
                  header: "",
                  align: "right",
                  cell: (c) => (
                    <Can permission="partners.edit">
                      <Button size="sm" variant="danger" onClick={() => setRevoking(c.name)}>
                        Revoke
                      </Button>
                    </Can>
                  ),
                },
              ]}
            />
            <FormError>{revoke.error?.message}</FormError>
          </>
        )}
      </Panel>

      {addingKey && partnerId !== null && (
        <AddKeyDialog partnerId={partnerId} onClose={() => setAddingKey(false)} />
      )}

      {revoking && (
        <ConfirmDialog
          title="Revoke this API key?"
          body={
            <>
              Anything the partner calls with the key{" "}
              <strong className="font-medium text-ink-800">{revoking}</strong> will stop
              authenticating immediately.
            </>
          }
          confirmLabel="Revoke key"
          onConfirm={async () => {
            await revoke.mutateAsync(revoking);
          }}
          onClose={() => setRevoking(null)}
        />
      )}
    </div>
  );
}

/** Issuing a key: its own step, because the secret is readable exactly once. */
function AddKeyDialog({ partnerId, onClose }: { partnerId: number; onClose: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [issuedKey, setIssuedKey] = useState("");

  const add = useMutation({
    mutationFn: () => api.addPartnerCredential(partnerId, name),
    onSuccess: ({ key }) => {
      setIssuedKey(key);
      void queryClient.invalidateQueries({ queryKey: ["partner", partnerId] });
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    add.mutate();
  };

  if (issuedKey)
    return (
      <Dialog title="API key created" onClose={onClose}>
        <div className="space-y-4">
          <CopyField value={issuedKey} label={`Key "${name}"`} />
          <p className="text-[13px] text-ink-500">
            Copy it now and share it with the partner — for security, the full key is never shown
            again.
          </p>
          <div className="flex justify-end">
            <Button variant="primary" onClick={onClose}>
              Done
            </Button>
          </div>
        </div>
      </Dialog>
    );

  return (
    <Dialog title="New API key" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Key name" htmlFor="ak-name" hint="What this key is for — the partner may hold several.">
          <TextInput
            id="ak-name"
            required
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Production"
          />
        </Field>
        <FormError>{add.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={add.isPending}>
            Generate key
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
