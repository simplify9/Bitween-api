import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UserPlus } from "lucide-react";
import { api, type PortalInvite, type PortalPerson } from "../../api";
import { keys } from "../../api/queryKeys";
import { useSessionCan } from "../../auth/useSessionCan";
import { Badge, Button, FormError, LoadError, LoadingBlock } from "../../components/ui/basics";
import { CopyField } from "../../components/ui/CopyField";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { ConfirmDialog, Dialog } from "../../components/ui/overlays";
import { Panel } from "../../components/ui/Panel";
import { PageHeader } from "../../components/layout/PageHeader";
import { Table } from "../../components/ui/Table";
import { formatDateTime, timeAgo } from "../../lib/dates";

/**
 * A partner's admin's own company: who can sign in to the portal, and with which role. They invite
 * people, change their roles, and disable or remove them. Only our team makes or changes one of
 * their admins, so another admin's row has nothing to change.
 */
export function PortalPeoplePage() {
  const canEdit = useSessionCan("portal-people.edit");
  const queryClient = useQueryClient();
  const people = useQuery({ queryKey: keys.portal.people, queryFn: () => api.listPortalPeople() });
  const roles = useQuery({ queryKey: keys.portal.peopleRoles, queryFn: () => api.listPortalPeopleRoles(), enabled: canEdit });
  const [adding, setAdding] = useState(false);
  const [invite, setInvite] = useState<{ name: string; result: PortalInvite } | null>(null);
  const [removing, setRemoving] = useState<PortalPerson | null>(null);

  const refresh = () => queryClient.invalidateQueries({ queryKey: keys.portal.people });
  const setRole = useMutation({
    mutationFn: ({ id, roleId }: { id: number; roleId: number }) => api.setPortalPersonRole(id, roleId),
    onSettled: refresh,
  });
  const setDisabled = useMutation({
    mutationFn: ({ id, disabled }: { id: number; disabled: boolean }) => api.setPortalPersonDisabled(id, disabled),
    onSettled: refresh,
  });
  const resend = useMutation({
    mutationFn: (p: PortalPerson) => api.invitePortalPersonAgain(p.id),
    onSuccess: (result, p) => {
      setInvite({ name: p.name, result });
      void refresh();
    },
  });

  const status = (p: PortalPerson) =>
    p.disabled ? (
      <Badge tone="neutral" title="Can't sign in until they're enabled again.">
        Disabled
      </Badge>
    ) : p.invited ? (
      p.inviteExpiresOn && new Date(p.inviteExpiresOn) < new Date() ? (
        <Badge tone="warn" title="Their invite stopped working before they used it. Send another.">
          Invite expired
        </Badge>
      ) : (
        <Badge tone="warn" title="Invited, and hasn't chosen a password yet.">
          Invited
        </Badge>
      )
    ) : (
      <Badge tone="ok" title="Can sign in.">
        Active
      </Badge>
    );

  return (
    <div className="max-w-5xl">
      <PageHeader title="People" description="Who at your company can sign in to this portal, and with which role." />
        <Panel
          title="Your company's people"
          description="Only your company's. Only we can make or change one of your admins."
          action={
            canEdit && (
              <Button
                variant="primary"
                size="sm"
                disabled={!roles.data}
                onClick={() => setAdding(true)}
                title="Add someone and email them an invite"
              >
                <UserPlus className="size-4" /> Invite someone
              </Button>
            )
          }
        >
          {people.isPending ? (
            <LoadingBlock />
          ) : people.isError ? (
            <LoadError error={people.error} what="your company's people" onRetry={() => void people.refetch()} />
          ) : (
            <>
              {/* No phone cards: a card has no room for the role picker and the actions, so the table scrolls instead. */}
              {roles.isError && (
                <LoadError error={roles.error} what="the roles you can give" onRetry={() => void roles.refetch()} />
              )}
              <Table
                rows={people.data}
                rowKey={(p) => p.id}
                columns={[
                  {
                    header: "Name",
                    wrap: true,
                    cell: (p) => (
                      <div>
                        <div className="font-medium text-ink-900">
                          {p.name}
                          {p.isYou && <span className="ml-1.5 text-xs font-normal text-ink-500">(you)</span>}
                        </div>
                        <div className="text-xs text-ink-500">{p.email}</div>
                      </div>
                    ),
                  },
                  {
                    header: "Role",
                    headerTitle: "What they can see and do in the portal",
                    cell: (p) =>
                      p.canChange && roles.data ? (
                        <div className="w-44">
                          <Select
                            aria-label={`Role of ${p.name}`}
                            value={p.role ? String(p.role.id) : ""}
                            disabled={setRole.isPending}
                            onChange={(e) => setRole.mutate({ id: p.id, roleId: Number(e.target.value) })}
                            options={[
                              ...(p.role ? [] : [{ value: "", label: "Choose a role" }]),
                              ...roles.data.map((r) => ({ value: String(r.id), label: r.name })),
                            ]}
                          />
                        </div>
                      ) : (
                        <span title={p.canChange ? undefined : "Only we can change this"}>{p.role?.name ?? "Several roles"}</span>
                      ),
                  },
                  { header: "Status", headerTitle: "Whether they can sign in", cell: status },
                  {
                    header: "Last signed in",
                    headerTitle: "When they last signed in to the portal",
                    cell: (p) =>
                      p.lastSignInOn ? <span title={formatDateTime(p.lastSignInOn)}>{timeAgo(p.lastSignInOn)}</span> : "-",
                  },
                  {
                    header: "",
                    align: "right",
                    cell: (p) =>
                      p.canChange && (
                        <div className="flex justify-end gap-1.5">
                          {p.invited && !p.disabled && (
                            <Button
                              size="sm"
                              busy={resend.isPending && resend.variables?.id === p.id}
                              onClick={() => resend.mutate(p)}
                              title="Send a new invite; the last one stops working"
                            >
                              Send invite again
                            </Button>
                          )}
                          <Button
                            size="sm"
                            busy={setDisabled.isPending && setDisabled.variables?.id === p.id}
                            onClick={() => setDisabled.mutate({ id: p.id, disabled: !p.disabled })}
                            title={p.disabled ? "Let them sign in again" : "Stop them signing in, and end their sessions"}
                          >
                            {p.disabled ? "Enable" : "Disable"}
                          </Button>
                          <Button size="sm" onClick={() => setRemoving(p)} title="Remove them for good">
                            Remove
                          </Button>
                        </div>
                      ),
                  },
                ]}
              />
              <FormError>{(setRole.error ?? setDisabled.error ?? resend.error)?.message}</FormError>
            </>
          )}
        </Panel>

      {adding && roles.data && (
        <AddPersonDialog
          roles={roles.data}
          onClose={() => setAdding(false)}
          onAdded={(name, result) => {
            setAdding(false);
            setInvite({ name, result });
            void refresh();
          }}
        />
      )}
      {invite && <InviteSentDialog name={invite.name} result={invite.result} onClose={() => setInvite(null)} />}
      {removing && (
        <ConfirmDialog
          title={`Remove ${removing.name}?`}
          body="They can't sign in any more, and their account is gone for good. You can invite them again later."
          confirmLabel="Remove"
          onConfirm={async () => {
            await api.removePortalPerson(removing.id);
            await refresh();
          }}
          onClose={() => setRemoving(null)}
        />
      )}
    </div>
  );
}

function AddPersonDialog({
  roles,
  onClose,
  onAdded,
}: {
  roles: { id: number; name: string; description: string | null }[];
  onClose: () => void;
  onAdded: (name: string, result: PortalInvite) => void;
}) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [roleId, setRoleId] = useState(roles[0]?.id ?? 0);
  const add = useMutation({
    mutationFn: () => api.addPortalPerson({ name: name.trim(), email: email.trim(), roleId }),
    onSuccess: (result) => onAdded(name.trim(), result),
  });
  const role = roles.find((r) => r.id === roleId);

  return (
    <Dialog title="Invite someone" onClose={onClose}>
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault();
          add.mutate();
        }}
      >
        <Field label="Name">
          <TextInput value={name} onChange={(e) => setName(e.target.value)} maxLength={200} required />
        </Field>
        <Field label="Email" hint="They get an email to choose their password.">
          <TextInput type="email" value={email} onChange={(e) => setEmail(e.target.value)} maxLength={200} required />
        </Field>
        <Field label="Role" hint={role?.description ?? undefined}>
          <Select
            value={String(roleId)}
            onChange={(e) => setRoleId(Number(e.target.value))}
            options={roles.map((r) => ({ value: String(r.id), label: r.name }))}
          />
        </Field>
        <FormError>{add.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={add.isPending} disabled={!name.trim() || !email.trim() || !roleId}>
            Invite
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

function InviteSentDialog({ name, result, onClose }: { name: string; result: PortalInvite; onClose: () => void }) {
  return (
    <Dialog title={result.emailed ? "Invite sent" : "Pass on this invite"} onClose={onClose}>
      <div className="space-y-3 text-sm text-ink-700">
        {result.emailed ? (
          <p>{name} has an email to choose their password.</p>
        ) : (
          <>
            <p>
              {result.emailError
                ? `The email to ${name} couldn't be sent (${result.emailError}).`
                : `There's no mail server to email ${name}.`}{" "}
              Send them this link to choose their password. It's shown only now.
            </p>
            {result.link && <CopyField value={result.link} label="Invite link" />}
          </>
        )}
        {result.expiresOn && <p className="text-[13px] text-ink-500">It works until {formatDateTime(result.expiresOn)}.</p>}
        <div className="flex justify-end">
          <Button variant="primary" onClick={onClose}>
            Done
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
