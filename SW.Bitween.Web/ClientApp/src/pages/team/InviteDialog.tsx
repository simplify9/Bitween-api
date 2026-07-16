import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { MailCheck } from "lucide-react";
import { api, ApiRequestError, type Invite } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { CopyField } from "../../components/ui/CopyField";

export function InviteDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: ["roles"], queryFn: () => api.listRoles() });

  const [email, setEmail] = useState("");
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [sent, setSent] = useState<Invite | null>(null);

  const invite = useMutation({
    mutationFn: () => api.inviteUser({ email, roleIds }),
    onSuccess: (created) => {
      setSent(created);
      void queryClient.invalidateQueries({ queryKey: ["users"] });
      void queryClient.invalidateQueries({ queryKey: ["roles"] });
    },
  });

  const toggleRole = (id: string) =>
    setRoleIds((prev) => (prev.includes(id) ? prev.filter((r) => r !== id) : [...prev, id]));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    invite.mutate();
  };

  if (sent) {
    return (
      <Dialog title="Invite sent" onClose={onClose}>
        <div className="space-y-4">
          <div className="flex items-start gap-3">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-ok-100 text-ok-600">
              <MailCheck className="size-4.5" />
            </span>
            <p className="text-sm text-ink-600">
              <strong className="font-medium text-ink-800">{sent.email}</strong> can now join by
              opening the invite link. It expires in 7 days.
            </p>
          </div>
          <div className="rounded-xl border border-dashed border-ink-300 p-3.5">
            <p className="mb-2 text-[13px] text-ink-500">
              Prototype: email isn't wired up, so share the link directly.
            </p>
            <CopyField value={`${location.origin}${import.meta.env.BASE_URL}invite/${sent.token}`} label="Invite link" />
          </div>
          <div className="flex justify-end">
            <Button variant="primary" onClick={onClose}>
              Done
            </Button>
          </div>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog title="Invite a member" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Email" htmlFor="invite-email" hint="They'll get a link to set their own password.">
          <TextInput
            id="invite-email"
            type="email"
            required
            autoFocus
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="colleague@company.com"
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
                onChange={() => toggleRole(role.id)}
              />
            ))}
          </div>
        </fieldset>

        <FormError>
          {invite.error instanceof ApiRequestError ? invite.error.message : invite.error?.message}
        </FormError>

        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={invite.isPending}>
            Send invite
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
