import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { KeyRound, Plus, Trash2 } from "lucide-react";
import { api, type Connection, type ConnectionGrant, type ConnectionInput, type ConnectionStatus } from "../../api";
import { Can } from "../../auth/guards";
import { useSessionCan } from "../../auth/useSessionCan";
import { HistoryCard } from "../../components/config/HistoryCard";
import { PageHeader } from "../../components/layout/PageHeader";
import { Badge, Button, EmptyState, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { BackLink } from "../../components/ui/BackLink";
import { Field, PasswordInput, Select, TextInput } from "../../components/ui/forms";
import { isNotFound } from "../../components/ui/isNotFound";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { Table } from "../../components/ui/Table";
import { keys } from "../../api/queryKeys";
import { usePageTitle } from "../../lib/pageTitle";
import { AuthLayout } from "../auth/AuthLayout";

const PRIVATE = "__private__";

const GRANTS: { value: ConnectionGrant; label: string; hint: string }[] = [
  {
    value: "AuthorizationCode",
    label: "Approve once (authorization code)",
    hint: "Someone approves access at the provider once; Bitween keeps the connection going from then on. Salesforce, Zoho, Shopify and Dynamics apps.",
  },
  {
    value: "ClientCredentials",
    label: "Client ID and secret (client credentials)",
    hint: "The client alone gets tokens; nobody has to approve anything. ServiceNow and most partner APIs.",
  },
  { value: "Password", label: "Service user (password grant)", hint: "A service user's name and password, for providers that still allow it." },
];

const STATUS: Record<ConnectionStatus, { tone: "ok" | "warn" | "danger" | "neutral"; label: string }> = {
  Connected: { tone: "ok", label: "Connected" },
  NeedsAuthorizing: { tone: "warn", label: "Needs authorizing" },
  Error: { tone: "danger", label: "Error" },
  New: { tone: "neutral", label: "Not used yet" },
};

export function StatusBadge({ status }: { status: ConnectionStatus }) {
  const s = STATUS[status] ?? STATUS.New;
  return <Badge tone={s.tone}>{s.label}</Badge>;
}

const blank: ConnectionInput = { name: "", grant: "AuthorizationCode", tokenAuth: "post" };

const inputOf = (c: Connection): ConnectionInput => ({
  name: c.name,
  grant: c.grant,
  authorizeUrl: c.authorizeUrl,
  tokenUrl: c.tokenUrl,
  clientId: c.clientId,
  clientSecret: c.clientSecret,
  scopes: c.scopes,
  authorizeParameters: c.authorizeParameters,
  tokenAuth: c.tokenAuth ?? "post",
  username: c.username,
  password: c.password,
});

/** The fields, for a new connection and an existing one alike. */
function ConnectionFields({
  value,
  onChange,
  disabled,
}: {
  value: ConnectionInput;
  onChange: (next: ConnectionInput) => void;
  disabled?: boolean;
}) {
  const set = (patch: Partial<ConnectionInput>) => onChange({ ...value, ...patch });
  const grant = GRANTS.find((g) => g.value === value.grant) ?? GRANTS[0];
  // A stored secret comes back masked; editing the field replaces it, leaving it keeps it.
  const secretValue = (v?: string | null) => (v === PRIVATE ? "" : (v ?? ""));
  const secretPlaceholder = (v?: string | null) => (v === PRIVATE ? "Saved - type to replace it" : "");

  return (
    <div className="space-y-4">
      <Field label="Name" htmlFor="cn-name" hint={`Settings write {{connections.${value.name || "NAME"}.accessToken}}.`}>
        <TextInput id="cn-name" required disabled={disabled} value={value.name} placeholder="salesforce"
          onChange={(e) => set({ name: e.target.value })} />
      </Field>
      <Field label="How it gets tokens" htmlFor="cn-grant" hint={grant.hint}>
        <Select id="cn-grant" disabled={disabled} value={value.grant} options={GRANTS}
          onChange={(e) => set({ grant: e.target.value as ConnectionGrant })} />
      </Field>
      {value.grant === "AuthorizationCode" && (
        <Field label="Authorize URL" htmlFor="cn-authorize" hint="The provider's page that asks for approval.">
          <TextInput id="cn-authorize" required disabled={disabled} value={value.authorizeUrl ?? ""}
            placeholder="https://login.salesforce.com/services/oauth2/authorize"
            onChange={(e) => set({ authorizeUrl: e.target.value })} />
        </Field>
      )}
      <Field label="Token URL" htmlFor="cn-token">
        <TextInput id="cn-token" required disabled={disabled} value={value.tokenUrl ?? ""}
          placeholder="https://login.salesforce.com/services/oauth2/token" onChange={(e) => set({ tokenUrl: e.target.value })} />
      </Field>
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Client ID" htmlFor="cn-client">
          <TextInput id="cn-client" required disabled={disabled} value={value.clientId ?? ""}
            onChange={(e) => set({ clientId: e.target.value })} />
        </Field>
        <Field label="Client secret" htmlFor="cn-secret">
          <PasswordInput id="cn-secret" disabled={disabled} value={secretValue(value.clientSecret)}
            placeholder={secretPlaceholder(value.clientSecret)} autoComplete="off"
            onChange={(e) => set({ clientSecret: e.target.value })} />
        </Field>
      </div>
      {value.grant === "Password" && (
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Username" htmlFor="cn-user">
            <TextInput id="cn-user" required disabled={disabled} value={value.username ?? ""} autoComplete="off"
              onChange={(e) => set({ username: e.target.value })} />
          </Field>
          <Field label="Password" htmlFor="cn-password">
            <PasswordInput id="cn-password" disabled={disabled} value={secretValue(value.password)}
              placeholder={secretPlaceholder(value.password)} autoComplete="off"
              onChange={(e) => set({ password: e.target.value })} />
          </Field>
        </div>
      )}
      <Field label="Scopes" htmlFor="cn-scopes" hint="Separated by spaces. Some providers give a refresh token only for one, such as offline_access or refresh_token.">
        <TextInput id="cn-scopes" disabled={disabled} value={value.scopes ?? ""} onChange={(e) => set({ scopes: e.target.value })} />
      </Field>
      {value.grant === "AuthorizationCode" && (
        <Field label="Extra approval parameters" htmlFor="cn-params" hint="Added to the approval page's address, as access_type=offline&prompt=consent, which Google needs.">
          <TextInput id="cn-params" disabled={disabled} value={value.authorizeParameters ?? ""}
            onChange={(e) => set({ authorizeParameters: e.target.value })} />
        </Field>
      )}
      <Field label="Client authentication" htmlFor="cn-auth" hint="How the client ID and secret are sent to the token URL.">
        <Select id="cn-auth" disabled={disabled} value={value.tokenAuth ?? "post"}
          options={[
            { value: "post", label: "In the form (client_secret_post)" },
            { value: "basic", label: "In an Authorization header (client_secret_basic)" },
          ]}
          onChange={(e) => set({ tokenAuth: e.target.value })} />
      </Field>
    </div>
  );
}

/** The secret as the API takes it: unchanged when the field was left empty over a saved one. */
const toSave = (draft: ConnectionInput, stored?: Connection): ConnectionInput => ({
  ...draft,
  clientSecret: draft.clientSecret === "" && stored?.clientSecret === PRIVATE ? PRIVATE : draft.clientSecret,
  password: draft.password === "" && stored?.password === PRIVATE ? PRIVATE : draft.password,
});

function CreateConnectionDialog({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<ConnectionInput>(blank);
  const create = useMutation({
    mutationFn: () => api.createConnection(draft),
    onSuccess: (c) => {
      void queryClient.invalidateQueries({ queryKey: keys.connections.all });
      navigate(`/connections/${c.id}`, { replace: true });
    },
  });
  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };
  return (
    <Dialog title="New connection" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <ConnectionFields value={draft} onChange={setDraft} />
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create connection
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

export function ConnectionsPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const creating = params.get("new") === "1";
  const connections = useQuery({ queryKey: keys.connections.list, queryFn: () => api.listConnections() });

  const createAction = (
    <Can permission="global-values.create">
      <Button variant="primary" onClick={() => setParams({ new: "1" })}>
        <Plus className="size-4" /> New connection
      </Button>
    </Can>
  );

  return (
    <div>
      <PageHeader
        title="Connections"
        description="Sign-ins Bitween keeps fresh for adapters: OAuth2 tokens it asks for, renews and rotates."
        help={{
          title: "How connections work",
          body: (
            <>
              <p>
                A connection is an OAuth2 client. Write <code>{"{{connections.NAME.accessToken}}"}</code> in any adapter
                setting, such as a bearer token, and the adapter gets a valid token each time it runs. Bitween renews it
                before it expires, and keeps the new refresh token when the provider rotates it.
              </p>
              <p>
                <code>{"{{connections.NAME.authorization}}"}</code> gives <code>Bearer</code> and the token, for a header.
                Tokens are never stored on exchanges, so a retry hours later still gets one that works.
              </p>
            </>
          ),
        }}
        actions={createAction}
      />
      {connections.isPending ? (
        <LoadingBlock label="Loading connections…" />
      ) : connections.isError ? (
        <LoadError error={connections.error} what="connections" onRetry={() => void connections.refetch()} />
      ) : connections.data.length === 0 ? (
        <EmptyState icon={<KeyRound />} title="No connections yet" action={createAction}>
          Add one to let adapters sign in to an API with OAuth2, without handling tokens themselves.
        </EmptyState>
      ) : (
        <Table
          rows={connections.data}
          rowKey={(c) => c.id}
          onRowClick={(c) => navigate(`/connections/${c.id}`)}
          columns={[
            { header: "Connection", cell: (c) => <span className="font-medium text-ink-900">{c.name}</span> },
            { header: "Gets tokens by", cell: (c) => <span className="text-ink-600">{GRANTS.find((g) => g.value === c.grant)?.label}</span> },
            { header: "Status", cell: (c) => <StatusBadge status={c.status} /> },
            { header: "Reference", cell: (c) => <span className="font-mono text-xs text-ink-500">{c.reference}</span> },
          ]}
        />
      )}
      {creating && <CreateConnectionDialog onClose={() => setParams({})} />}
    </div>
  );
}

export function ConnectionPage() {
  const id = Number(useParams().id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const canEdit = useSessionCan("global-values.edit");
  const connection = useQuery({ queryKey: keys.connections.detail(id), queryFn: () => api.getConnection(id), retry: false });
  usePageTitle(connection.data?.name);

  const [draft, setDraft] = useState<ConnectionInput | null>(null);
  const [deleting, setDeleting] = useState(false);
  useEffect(() => {
    if (connection.data && draft === null) setDraft(inputOf(connection.data));
  }, [connection.data, draft]);

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: keys.connections.all });
    setDraft(null);
  };
  const save = useMutation({ mutationFn: () => api.updateConnection(id, toSave(draft!, connection.data)), onSuccess: refresh });
  const test = useMutation({ mutationFn: () => api.testConnection(id), onSuccess: refresh });
  const authorize = useMutation({
    mutationFn: () => api.authorizeConnection(id),
    onSuccess: (url) => window.location.assign(url),
  });

  if (connection.isPending) return <LoadingBlock label="Loading connection…" />;
  if (connection.isError)
    return isNotFound(connection.error) ? (
      <EmptyState icon={<KeyRound />} title="Connection not found">
        <Link to="/connections" className="font-medium text-crimson-700 hover:underline">Back to connections</Link>
      </EmptyState>
    ) : (
      <LoadError error={connection.error} what="the connection" onRetry={() => void connection.refetch()} />
    );

  const c = connection.data;
  return (
    <div>
      <BackLink to="/connections" label="Connections" />
      <PageHeader
        title={c.name}
        description={c.reference}
        actions={
          <div className="flex gap-2">
            {c.grant === "AuthorizationCode" && (
              <Can permission="global-values.edit">
                <Button variant={c.status === "Connected" ? undefined : "primary"} busy={authorize.isPending} onClick={() => authorize.mutate()}>
                  {c.status === "Connected" ? "Authorize again" : "Authorize"}
                </Button>
              </Can>
            )}
            <Can permission="global-values.edit">
              <Button busy={test.isPending} onClick={() => test.mutate()}>Test</Button>
            </Can>
            <Can permission="global-values.delete">
              <Button variant="ghost" aria-label="Delete connection" onClick={() => setDeleting(true)}>
                <Trash2 className="size-4" />
              </Button>
            </Can>
          </div>
        }
      />
      <div className="grid gap-5 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <Panel title="Settings" description="Changing who the client is, or where its tokens come from, forgets the tokens it has.">
          {draft && (
            <form onSubmit={(e) => { e.preventDefault(); save.mutate(); }} className="space-y-4">
              <ConnectionFields value={draft} onChange={setDraft} disabled={!canEdit} />
              <FormError>{save.error?.message}</FormError>
              {canEdit && (
                <div className="flex justify-end">
                  <Button type="submit" variant="primary" busy={save.isPending}>Save</Button>
                </div>
              )}
            </form>
          )}
        </Panel>
        <div className="min-w-0 space-y-5">
          <Panel title="Status">
            <dl className="space-y-2 text-sm">
              <div className="flex justify-between gap-3"><dt className="text-ink-500">Status</dt><dd><StatusBadge status={c.status} /></dd></div>
              {c.accessTokenExpiresOn && (
                <div className="flex justify-between gap-3"><dt className="text-ink-500">Token good until</dt>
                  <dd className="text-ink-800">{new Date(c.accessTokenExpiresOn).toLocaleString()}</dd></div>
              )}
              {c.grant === "AuthorizationCode" && (
                <div className="flex justify-between gap-3"><dt className="text-ink-500">Refresh token</dt>
                  <dd className="text-ink-800">{c.hasRefreshToken ? "Kept" : "None"}</dd></div>
              )}
              {c.authorizedOn && (
                <div className="flex justify-between gap-3"><dt className="text-ink-500">Authorized</dt>
                  <dd className="text-right text-ink-800">{new Date(c.authorizedOn).toLocaleString()}{c.authorizedBy ? ` by ${c.authorizedBy}` : ""}</dd></div>
              )}
            </dl>
            {c.lastError && <p className="mt-3 rounded-lg bg-danger-50 p-2.5 text-[13px] text-danger-700">{c.lastError}</p>}
            <FormError>{test.error?.message ?? authorize.error?.message}</FormError>
          </Panel>
          <HistoryCard entityName="Connection" entityKey={String(id)} />
        </div>
      </div>
      {deleting && (
        <ConfirmDialog
          title="Delete this connection?"
          body={<><strong className="font-medium text-ink-800">{c.name}</strong> and its tokens will be gone. A connection settings still refer to can't be deleted.</>}
          confirmLabel="Delete connection"
          onConfirm={async () => {
            await api.deleteConnection(id);
            void queryClient.invalidateQueries({ queryKey: keys.connections.all });
            navigate("/connections", { replace: true });
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </div>
  );
}

/**
 * Where a provider sends the browser back after approving a connection. The code it brings is
 * handed to the server, which keeps the tokens; a code is good once, so this runs once.
 */
export function OAuthCallbackPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();
  const [error, setError] = useState("");
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    const code = params.get("code");
    const state = params.get("state");
    const refused = params.get("error_description") || params.get("error");
    if (refused || !code || !state) {
      setError(refused ? `The provider didn't approve the connection: ${refused}` : "The provider sent no code.");
      return;
    }
    api
      .completeConnection(code, state)
      .then(async ({ id }) => {
        await queryClient.invalidateQueries({ queryKey: keys.connections.all });
        navigate(`/connections/${id}`, { replace: true });
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : "Authorizing failed."));
  }, [params, navigate, queryClient]);

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold tracking-tight text-ink-900">Authorizing the connection</h1>
      {error ? (
        <div className="mt-4 space-y-4">
          <FormError>{error}</FormError>
          <Link to="/connections" className="text-sm font-medium text-crimson-700 hover:underline">Back to connections</Link>
        </div>
      ) : (
        <LoadingBlock />
      )}
    </AuthLayout>
  );
}
