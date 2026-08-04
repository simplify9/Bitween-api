import { type FormEvent, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api, type InformationTypeRow } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { InfoTypePicker } from "../../components/config/pickers";

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
      void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
      const base = `/bus-gateways/${gateway.id}`;
      navigate(base);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <div>
      <Link
        to={"/subscriptions?types=bus-gateways"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {"Integrations"}
      </Link>

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New bus gateway</h1>
      <p className="mt-1 text-sm text-ink-500">
        Listens for one information type on the message bus. Add routes afterwards on its page.
      </p>

      <form onSubmit={submit} className="mt-6 max-w-lg space-y-5 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="nbg-name">
          <TextInput
            id="nbg-name"
            required
            autoFocus
            value={draft.name}
            onChange={(e) => update({ name: e.target.value })}
            placeholder="e.g. ERP events"
          />
        </Field>

        <section>
          <h2 className="mb-1 text-[13px] font-medium text-ink-700">Listens for</h2>
          <p className="mb-3 text-[13px] text-ink-500">
            Only bus-enabled information types can be listened for. The choice is permanent.
          </p>
          <InfoTypePicker
            value={draft.informationTypeId}
            onChange={(id) => update({ informationTypeId: id })}
            filter={busEnabled}
            busRequired
          />
        </section>

        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end">
          <Button
            type="submit"
            variant="primary"
            busy={create.isPending}
            disabled={!draft.name.trim() || draft.informationTypeId === null}
          >
            Create gateway
          </Button>
        </div>
      </form>
    </div>
  );
}
