import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiRequestError } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Checkbox, Field, PasswordInput, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { keys } from "../../api/queryKeys";

export function AddMemberDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient();
  const roles = useQuery({ queryKey: keys.roles.list, queryFn: () => api.listRoles() });

  const [displayName, setDisplayName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [roleIds, setRoleIds] = useState<string[]>([]);

  const create = useMutation({
    mutationFn: () => api.createUser({ displayName, email, password, roleIds }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: keys.users.all });
      void queryClient.invalidateQueries({ queryKey: keys.roles.all });
      onClose();
    },
  });

  const toggleRole = (id: string) =>
    setRoleIds((prev) => (prev.includes(id) ? prev.filter((r) => r !== id) : [...prev, id]));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <Dialog title="Add a member" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Name" htmlFor="member-name">
          <TextInput
            id="member-name"
            required
            autoFocus
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Jane Cooper"
          />
        </Field>

        <Field label="Email" htmlFor="member-email">
          <TextInput
            id="member-email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="colleague@company.com"
          />
        </Field>

        <Field
          label="Password"
          htmlFor="member-password"
          hint="Pass this on to them yourself — Bitween doesn't send mail. They can change it from their profile."
        >
          <PasswordInput
            id="member-password"
            required
            minLength={8}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="At least 8 characters"
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
          {create.error instanceof ApiRequestError ? create.error.message : create.error?.message}
        </FormError>

        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Add member
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
