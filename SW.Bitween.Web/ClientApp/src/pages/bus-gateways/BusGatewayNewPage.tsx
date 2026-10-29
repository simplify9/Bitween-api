import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, type InformationTypeRow } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { InfoTypePicker } from "../../components/config/pickers";
import { BackLink } from "../../components/ui/BackLink";
import { StillNeeds, useFormCheck } from "../../components/ui/formCheck";
import { keys } from "../../api/queryKeys";

/** Local draft state with the patch-and-clear shape the form body uses. */
function useDraft<T extends object>(initial: T) {
  const [draft, setDraft] = useState<T>(initial);
  const update = (patch: Partial<T>) => setDraft((d) => ({ ...d, ...patch }));
  const clear = () => setDraft(initial);
  return [draft, update, clear] as const;
}


interface Draft {
  name: string;
  informationTypeId: number | null;
}

const EMPTY: Draft = { name: "", informationTypeId: null };
const busEnabled = (t: InformationTypeRow) => t.busEnabled;

export function BusGatewayNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [draft, update, clear] = useDraft<Draft>(EMPTY);


  const create = useMutation({
    mutationFn: () => api.createBusGateway({ name: draft.name, informationTypeId: draft.informationTypeId! }),
    onSuccess: (gateway) => {
      clear();
      void queryClient.invalidateQueries({ queryKey: keys.busGateways.all });
      const base = `/bus-gateways/${gateway.id}`;
      navigate(base, { replace: true });
    },
  });

  const check = useFormCheck([
    {
      field: "name",
      failing: !draft.name.trim(),
      needs: "a name",
      message: "Give the gateway a name, e.g. ERP events.",
      focus: "nbg-name",
    },
    {
      field: "type",
      failing: draft.informationTypeId === null,
      needs: "an information type to listen for",
      message: "Pick the information type it listens for.",
      focus: "nbg-type",
    },
  ]);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    check.submit(() => create.mutate());
  };

  return (
    <div>
      <BackLink to="/bus-gateways" label="Bus gateways" />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New bus gateway</h1>
      <p className="mt-1 text-sm text-ink-500">
        Listens for one information type on the message bus. Add routes afterwards on its page.
      </p>

      <form noValidate onSubmit={submit} className="mt-6 max-w-lg space-y-5 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="nbg-name" error={check.errors.name}>
          <TextInput
            id="nbg-name"
            required
            autoFocus
            value={draft.name}
            onChange={(e) => update({ name: e.target.value })}
            placeholder="e.g. ERP events"
          />
        </Field>

        <Field
          label="Listens for"
          htmlFor="nbg-type"
          hint="Only bus-enabled information types can be listened for. The choice is permanent."
          error={check.errors.type}
        >
          <InfoTypePicker
            id="nbg-type"
            value={draft.informationTypeId}
            onChange={(id) => update({ informationTypeId: id })}
            filter={busEnabled}
            busRequired
          />
        </Field>

        <FormError>{create.error?.message}</FormError>
        <div className="flex items-center justify-end gap-3">
          <StillNeeds missing={check.missing} />
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create gateway
          </Button>
        </div>
      </form>
    </div>
  );
}
