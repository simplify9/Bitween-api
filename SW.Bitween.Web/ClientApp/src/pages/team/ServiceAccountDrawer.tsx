import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, Check, KeyRound, Power, PowerOff, Trash2, X } from "lucide-react";
import { api, type IssuedServiceToken, type ServiceAccount, type ServiceToken } from "../../api";
import { Can } from "../../auth/guards";
import { useSession } from "../../auth/useSession";
import { HistoryList } from "../../components/config/HistoryCard";
import { CopyField } from "../../components/ui/CopyField";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { Checkbox, Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog } from "../../components/ui/overlays";
import { useModalFocus } from "../../components/ui/useModalFocus";
import { formatDate, timeAgo, timeUntil } from "../../lib/dates";
import { keys } from "../../api/queryKeys";

/** As the server allows: at most two active at once, at most a year each. */
const MAX_ACTIVE_TOKENS = 2;
const LIFETIMES = [
  { value: "30", label: "30 days" },
  { value: "90", label: "90 days" },
  { value: "180", label: "180 days" },
  { value: "365", label: "1 year" },
];

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-t border-ink-100 px-5 py-4">
      <h3 className="mb-3 text-xs font-semibold tracking-wide text-ink-500 uppercase">{title}</h3>
      {children}
    </section>
  );
}

function tokenBadge(token: ServiceToken) {
  if (token.status === "revoked") return <Badge>Revoked</Badge>;
  if (token.status === "expired") return <Badge>Expired</Badge>;
  // Within two weeks of running out: the job using it is about to stop.
  const soon = new Date(token.expiresOn).getTime() - Date.now() < 14 * 24 * 3600 * 1000;
  return soon ? <Badge tone="warn">Expires {timeUntil(token.expiresOn)}</Badge> : <Badge tone="ok">Active</Badge>;
}

export function ServiceAccountDrawer({ accountId, onClose }: { accountId: number; onClose: () => void }) {
  const { can } = useSession();
  const queryClient = useQueryClient();
  const accounts = useQuery({ queryKey: keys.serviceAccounts.all, queryFn: () => api.listServiceAccounts() });
  const roles = useQuery({ queryKey: keys.roles.list, queryFn: () => api.listRoles() });

  const [draftRoleIds, setDraftRoleIds] = useState<string[] | null>(null);
  const [confirming, setConfirming] = useState<"remove" | ServiceToken | null>(null);
  const [tokenName, setTokenName] = useState("");
  const [lifetime, setLifetime] = useState("90");
  // Kept on screen until dismissed: it is never shown again.
  const [issued, setIssued] = useState<IssuedServiceToken | null>(null);

  const panel = useRef<HTMLElement>(null);
  useModalFocus(panel);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  const invalidate = () => void queryClient.invalidateQueries({ queryKey: keys.serviceAccounts.all });
  const account = accounts.data?.find((a) => a.id === accountId);

  const saveRoles = useMutation({
    mutationFn: (a: { account: ServiceAccount; roleIds: string[] }) =>
      api.updateServiceAccount(accountId, { name: a.account.name, roleIds: a.roleIds.map(Number) }),
    onSuccess: () => {
      setDraftRoleIds(null);
      invalidate();
    },
  });
  const setDisabled = useMutation({
    mutationFn: (disabled: boolean) => api.setServiceAccountDisabled(accountId, disabled),
    onSuccess: invalidate,
  });
  const issue = useMutation({
    mutationFn: () => api.issueServiceToken(accountId, { name: tokenName.trim(), expiresInDays: Number(lifetime) }),
    onSuccess: (token) => {
      setIssued(token);
      setTokenName("");
      invalidate();
    },
  });

  const body = () => {
    if (accounts.isPending) return <LoadingBlock label="Loading service account…" />;
    if (accounts.isError)
      return (
        <div className="p-5">
          <LoadError error={accounts.error} what="this service account" onRetry={() => void accounts.refetch()} />
        </div>
      );
    if (!account) return <p className="px-5 py-8 text-sm text-ink-500">This service account no longer exists.</p>;

    const accountRoleIds = account.roles.map((r) => String(r.id));
    const currentRoles = draftRoleIds ?? accountRoleIds;
    const rolesDirty =
      draftRoleIds !== null &&
      (draftRoleIds.length !== accountRoleIds.length || draftRoleIds.some((r) => !accountRoleIds.includes(r)));
    const editable = can("service-accounts.edit");
    const active = account.tokens.filter((t) => t.status === "active");

    const submitToken = (e: FormEvent) => {
      e.preventDefault();
      issue.mutate();
    };

    return (
      <>
        <div className="flex items-start gap-4 px-5 pt-5 pb-4">
          <div className="flex size-11 shrink-0 items-center justify-center rounded-full bg-ink-100 text-ink-600">
            <Bot className="size-5" aria-hidden />
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="truncate text-lg font-semibold tracking-tight text-ink-900">{account.name}</h2>
            <p className="text-[13px] text-ink-500">
              Service account, created {formatDate(account.createdOn)}. Last signed in{" "}
              {account.lastSignInOn ? timeAgo(account.lastSignInOn) : "never"}.
            </p>
            <div className="mt-1.5">
              {account.disabled ? <Badge>Disabled</Badge> : <Badge tone="ok">Enabled</Badge>}
            </div>
          </div>
        </div>

        <Section title="Tokens">
          {issued && (
            <div className="mb-4 space-y-2 rounded-lg border border-ok-200 bg-ok-50 p-3">
              <p className="flex items-center gap-1.5 text-[13px] font-medium text-ok-800">
                <Check className="size-4 shrink-0" aria-hidden />
                Token "{issued.name}" issued
              </p>
              <CopyField value={issued.token} label="Token" />
              <p className="text-[13px] text-ink-600">
                Copy it now and keep it somewhere safe, such as your cluster's secrets. Bitween keeps only a hash, so it
                can't be shown again.
              </p>
              <div className="flex justify-end">
                <Button size="sm" onClick={() => setIssued(null)}>
                  Done
                </Button>
              </div>
            </div>
          )}

          {account.tokens.length === 0 ? (
            <p className="text-sm text-ink-500">No tokens yet.</p>
          ) : (
            <ul className="divide-y divide-ink-100 rounded-lg border border-ink-200">
              {account.tokens.map((token) => (
                <li key={token.id} className="flex items-start gap-3 px-3 py-2.5">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span
                        className={`text-sm font-medium ${token.status === "active" ? "text-ink-900" : "text-ink-500"}`}
                      >
                        {token.name}
                      </span>
                      {tokenBadge(token)}
                    </div>
                    <p className="font-mono text-xs text-ink-500">{token.shown}…</p>
                    <p className="text-xs text-ink-500">
                      {token.status === "revoked" && token.revokedOn
                        ? `Revoked ${formatDate(token.revokedOn)}`
                        : token.status === "expired"
                          ? `Expired ${formatDate(token.expiresOn)}`
                          : `Expires ${formatDate(token.expiresOn)}`}
                      {" · "}
                      {token.lastUsedOn
                        ? `last used ${timeAgo(token.lastUsedOn)}${token.lastUsedFrom ? ` from ${token.lastUsedFrom}` : ""}`
                        : "never used"}
                    </p>
                  </div>
                  {editable && token.status === "active" && (
                    <Button size="sm" variant="danger" onClick={() => setConfirming(token)}>
                      Revoke
                    </Button>
                  )}
                </li>
              ))}
            </ul>
          )}

          <Can permission="service-accounts.create">
            {active.length >= MAX_ACTIVE_TOKENS ? (
              <p className="mt-3 text-[13px] text-ink-500">
                It has {MAX_ACTIVE_TOKENS} active tokens, the most it can hold. Revoke one to issue another.
              </p>
            ) : (
              <form onSubmit={submitToken} className="mt-4 space-y-3">
                <div className="grid grid-cols-[1fr_9rem] gap-2">
                  <Field label="New token" htmlFor="token-name">
                    <TextInput
                      id="token-name"
                      required
                      maxLength={100}
                      value={tokenName}
                      onChange={(e) => setTokenName(e.target.value)}
                      placeholder="What uses it"
                    />
                  </Field>
                  <Field label="Expires after" htmlFor="token-lifetime">
                    <Select
                      id="token-lifetime"
                      value={lifetime}
                      onChange={(e) => setLifetime(e.target.value)}
                      options={LIFETIMES}
                    />
                  </Field>
                </div>
                <FormError>{issue.error?.message}</FormError>
                <Button size="sm" type="submit" busy={issue.isPending}>
                  <KeyRound className="size-3.5" /> Issue token
                </Button>
              </form>
            )}
          </Can>
        </Section>

        <Section title="Roles">
          {editable ? (
            <div className="space-y-2.5">
              {(roles.data ?? []).map((role) => (
                <Checkbox
                  key={role.id}
                  label={role.name}
                  description={role.description}
                  checked={currentRoles.includes(role.id)}
                  onChange={() =>
                    setDraftRoleIds(
                      currentRoles.includes(role.id)
                        ? currentRoles.filter((r) => r !== role.id)
                        : [...currentRoles, role.id],
                    )
                  }
                />
              ))}
              <FormError>{saveRoles.error?.message}</FormError>
              {rolesDirty && (
                <div className="flex gap-2 pt-1">
                  <Button
                    size="sm"
                    variant="primary"
                    busy={saveRoles.isPending}
                    onClick={() => saveRoles.mutate({ account, roleIds: draftRoleIds! })}
                  >
                    Save roles
                  </Button>
                  <Button size="sm" onClick={() => setDraftRoleIds(null)}>
                    Discard
                  </Button>
                </div>
              )}
            </div>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {account.roles.map((r) => (
                <Badge key={r.id}>{r.name}</Badge>
              ))}
            </div>
          )}
        </Section>

        {(editable || can("service-accounts.delete")) && (
          <Section title="Account actions">
            <div className="space-y-3">
              {editable && (
                <div>
                  <Button size="sm" busy={setDisabled.isPending} onClick={() => setDisabled.mutate(!account.disabled)}>
                    {account.disabled ? (
                      <>
                        <Power className="size-3.5" /> Re-enable
                      </>
                    ) : (
                      <>
                        <PowerOff className="size-3.5" /> Disable
                      </>
                    )}
                  </Button>
                  <p className="mt-1 text-[13px] text-ink-500">
                    Disabling stops every token it holds at once, including sessions already signed in.
                  </p>
                  <FormError>{setDisabled.error?.message}</FormError>
                </div>
              )}
              <Can permission="service-accounts.delete">
                <Button size="sm" variant="danger" onClick={() => setConfirming("remove")}>
                  <Trash2 className="size-3.5" /> Remove service account
                </Button>
              </Can>
            </div>
          </Section>
        )}

        <Can permission="audit.view">
          <Section title="History">
            <HistoryList entityName="Account" entityKey={String(accountId)} />
            <Link
              to={`/audit?userId=${accountId}`}
              className="mt-2 inline-block text-[13px] font-medium link"
            >
              What this service account changed
            </Link>
          </Section>
        </Can>
      </>
    );
  };

  return (
    <div className="fixed inset-0 z-50">
      <div className="absolute inset-0 bg-ink-950/40" onClick={onClose} />
      <aside
        ref={panel}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label="Service account details"
        className="absolute inset-y-0 right-0 flex w-full max-w-md flex-col overflow-y-auto bg-white shadow-2xl outline-none"
      >
        <button
          onClick={onClose}
          aria-label="Close"
          className="absolute top-4 right-4 rounded-md p-1 text-ink-500 hover:bg-ink-100 hover:text-ink-700"
        >
          <X className="size-5" />
        </button>
        {body()}
      </aside>

      {confirming === "remove" && account && (
        <ConfirmDialog
          title="Remove this service account?"
          body={
            <>
              <strong className="font-medium text-ink-800">{account.name}</strong> and all of its tokens stop working
              immediately. This can't be undone.
            </>
          }
          confirmLabel="Remove service account"
          onConfirm={async () => {
            await api.deleteServiceAccount(accountId);
            invalidate();
            onClose();
          }}
          onClose={() => setConfirming(null)}
        />
      )}
      {confirming && confirming !== "remove" && (
        <ConfirmDialog
          title="Revoke this token?"
          body={
            <>
              Whatever uses <strong className="font-medium text-ink-800">{confirming.name}</strong> can't sign in with
              it any more. A session it already opened runs out within minutes; disable the account to end that now.
            </>
          }
          confirmLabel="Revoke token"
          onConfirm={async () => {
            await api.revokeServiceToken(accountId, confirming.id);
            invalidate();
          }}
          onClose={() => setConfirming(null)}
        />
      )}
    </div>
  );
}
