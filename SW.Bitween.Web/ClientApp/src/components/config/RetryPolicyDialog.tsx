import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api";
import { Button, FormError } from "../ui/basics";
import { Field, TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { keys } from "../../api/queryKeys";

/**
 * Creating a retry policy, as one component.
 *
 * A policy is born with nothing but a name — its budget, groups and alert routing are
 * configured on its own page, which is too much to ask for inside a dialog. So the two
 * callers want opposite things once it exists, and `onCreated` is what tells them apart:
 * the policies list sends you to the new policy's page to carry on configuring it, while
 * a subscription attaching one wants to stay where it is with the policy now selected.
 */
export function CreateRetryPolicyDialog({
  onClose,
  /** Given, the new policy is handed back and the dialog closes. Omitted, we navigate to it. */
  onCreated,
}: {
  onClose: () => void;
  onCreated?: (id: number) => void;
}) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");

  const create = useMutation({
    mutationFn: () => api.createRetryPolicy({ name }),
    onSuccess: (policy) => {
      void queryClient.invalidateQueries({ queryKey: keys.retryPolicies.all });
      if (onCreated) {
        onCreated(policy.id);
        onClose();
        return;
      }
      // replace, not push: the ?new=1 entry this dialog opened on is still behind us,
      // and Back onto it would reopen the form that was just submitted.
      navigate(`/retry-policies/${policy.id}`, { replace: true });
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <Dialog title="New retry policy" onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <Field label="Name" htmlFor="nrp-name" hint="Groups and budgets are added on the policy's page.">
          <TextInput
            id="nrp-name"
            required
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Transient failures"
          />
        </Field>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create policy
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
