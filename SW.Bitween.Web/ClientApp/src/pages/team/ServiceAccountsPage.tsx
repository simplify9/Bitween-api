import { useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, Plus } from "lucide-react";
import { api, ApiRequestError, type ServiceAccount } from "../../api";
import { Can } from "../../auth/guards";
import { PageHeader } from "../../components/layout/PageHeader";
import { clickableRow, clickableRowClass } from "../../components/ui/clickableRow";
import { Badge, Button, EmptyState, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { timeAgo, timeUntil } from "../../lib/dates";
import { keys } from "../../api/queryKeys";
import { ServiceAccountDrawer } from "./ServiceAccountDrawer";
import { usePageTitle } from "../../lib/pageTitle";

export const SERVICE_ACCOUNTS_PATH = "/team/service-accounts";

const activeTokens = (account: ServiceAccount) => account.tokens.filter((t) => t.status === "active");

export function ServiceAccountsPage() {
  const navigate = useNavigate();
  const { id: openId } = useParams();
  const [adding, setAdding] = useState(false);

  const accounts = useQuery({ queryKey: keys.serviceAccounts.all, queryFn: () => api.listServiceAccounts() });
  // The open account's drawer has its own address, so the breadcrumb names it.
  usePageTitle(openId ? accounts.data?.find((a) => String(a.id) === openId)?.name : null);

  return (
    <div>
      <PageHeader
        title="Service accounts"
        description="Accounts for programs, such as a job applying configuration or a pipeline publishing adapters. They sign in with tokens, never with a password."
        help={{
          title: "How service accounts work",
          body: (
            <>
              <p>
                A service account holds <strong>roles</strong> like a member does, and can do exactly what they allow.
                It can't sign in to this site.
              </p>
              <p>
                It signs in with a <strong>token</strong>, shown once when you issue it. Every token expires, and you
                can revoke one at any time; disabling the account stops all of its tokens at once. An account can hold
                two active tokens, so a new one can be put in place before the old one is revoked.
              </p>
              <p>
                With the bitween command, set <code>BITWEEN_URL</code> and <code>BITWEEN_TOKEN</code> and it signs in on
                its own.
              </p>
            </>
          ),
        }}
        actions={
          <Can permission="service-accounts.create">
            <Button variant="primary" onClick={() => setAdding(true)}>
              <Plus className="size-4" /> New service account
            </Button>
          </Can>
        }
      />

      {accounts.isPending ? (
        <LoadingBlock label="Loading service accounts…" />
      ) : accounts.isError ? (
        <LoadError error={accounts.error} what="service accounts" onRetry={() => void accounts.refetch()} />
      ) : accounts.data.length === 0 ? (
        <EmptyState
          icon={<Bot />}
          title="No service accounts yet"
          action={
            <Can permission="service-accounts.create">
              <Button variant="primary" onClick={() => setAdding(true)}>
                <Plus className="size-4" /> New service account
              </Button>
            </Can>
          }
        >
          Make one for each program that works with Bitween on its own, so each can be given only what it needs.
        </EmptyState>
      ) : (
        <div className="overflow-x-auto rounded-xl border border-ink-200 bg-white">
          <table className="w-full min-w-160 text-sm">
            <thead>
              <tr className="border-b border-ink-100 text-left text-xs text-ink-500">
                <th className="px-4 py-2.5 font-medium">Service account</th>
                <th className="px-4 py-2.5 font-medium">Roles</th>
                <th className="px-4 py-2.5 font-medium">Tokens</th>
                <th className="px-4 py-2.5 font-medium">Last signed in</th>
              </tr>
            </thead>
            <tbody>
              {accounts.data.map((account) => {
                const active = activeTokens(account);
                const soonest = active.map((t) => t.expiresOn).sort()[0];
                return (
                  <tr
                    key={account.id}
                    {...clickableRow(() => navigate(`${SERVICE_ACCOUNTS_PATH}/${account.id}`))}
                    className={`cursor-pointer border-b border-ink-100 last:border-b-0 hover:bg-ink-50 ${clickableRowClass}`}
                  >
                    <td className="px-4 py-2.5">
                      <div className="flex items-center gap-2">
                        <Bot className="size-4 shrink-0 text-ink-500" aria-hidden />
                        <span className={`font-medium ${account.disabled ? "text-ink-500" : "text-ink-900"}`}>
                          {account.name}
                        </span>
                        {account.disabled && <Badge>Disabled</Badge>}
                      </div>
                    </td>
                    <td className="px-4 py-2.5">
                      <div className="flex flex-wrap gap-1">
                        {account.roles.map((r) => (
                          <Badge key={r.id}>{r.name}</Badge>
                        ))}
                      </div>
                    </td>
                    <td className="px-4 py-2.5 text-ink-700">
                      {active.length === 0 ? (
                        <span className="text-ink-500">None active</span>
                      ) : (
                        <>
                          {active.length} active
                          <span className="text-ink-500">, next expires {timeUntil(soonest)}</span>
                        </>
                      )}
                    </td>
                    <td className="px-4 py-2.5 text-ink-500">
                      {account.lastSignInOn ? timeAgo(account.lastSignInOn) : "Never"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {adding && (
        <NewServiceAccountDialog
          onClose={() => setAdding(false)}
          onCreated={(id) => {
            setAdding(false);
            navigate(`${SERVICE_ACCOUNTS_PATH}/${id}`);
          }}
        />
      )}
      {openId && (
        <ServiceAccountDrawer key={openId} accountId={Number(openId)} onClose={() => navigate(SERVICE_ACCOUNTS_PATH)} />
      )}
    </div>
  );
}

function NewServiceAccountDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (id: number) => void }) {
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: keys.roles.list, queryFn: () => api.listRoles() });
  const [name, setName] = useState("");
  const [roleIds, setRoleIds] = useState<string[]>([]);

  const create = useMutation({
    mutationFn: () => api.createServiceAccount({ name: name.trim(), roleIds: roleIds.map(Number) }),
    onSuccess: (id) => {
      void queryClient.invalidateQueries({ queryKey: keys.serviceAccounts.all });
      onCreated(id);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <Dialog title="New service account" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field
          label="Name"
          htmlFor="service-account-name"
          hint="What it's for, so it's recognisable in the audit trail."
        >
          <TextInput
            id="service-account-name"
            required
            autoFocus
            maxLength={200}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="lab-apply"
          />
        </Field>

        <fieldset>
          <legend className="mb-2 block text-[13px] font-medium text-ink-700">Roles</legend>
          <div className="max-h-56 space-y-2.5 overflow-y-auto rounded-lg border border-ink-200 p-3">
            {(roles.data ?? []).map((role) => (
              <Checkbox
                key={role.id}
                label={role.name}
                description={role.description}
                checked={roleIds.includes(role.id)}
                onChange={() =>
                  setRoleIds((prev) =>
                    prev.includes(role.id) ? prev.filter((r) => r !== role.id) : [...prev, role.id],
                  )
                }
              />
            ))}
          </div>
          <p className="mt-1.5 text-[13px] text-ink-500">Give it only what its job needs. You issue its tokens next.</p>
        </fieldset>

        <FormError>{create.error instanceof ApiRequestError ? create.error.message : create.error?.message}</FormError>

        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
