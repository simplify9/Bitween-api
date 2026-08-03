import { useEffect, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api, type InformationTypeRow } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { InfoTypePicker } from "../../components/config/pickers";
import { usePersistentDraft } from "../../lib/persistentDraft";
import { takePicked, useHereAsReturnTarget, useReturnContext, withReturn } from "../../lib/returnTo";

interface Draft {
  name: string;
  informationTypeId: number | null;
}

const EMPTY: Draft = { name: "", informationTypeId: null };
const busEnabled = (t: InformationTypeRow) => t.busEnabled;

export function BusGatewayNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const here = useHereAsReturnTarget();
  const [params, setParams] = useSearchParams();

  const [draft, update, clear] = usePersistentDraft<Draft>("bitween-draft-new-bus-gateway", EMPTY);

  // returning from a "New information type" detour
  useEffect(() => {
    const picked = takePicked(params, "infotype");
    if (picked !== null) {
      update({ informationTypeId: picked });
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          next.delete("picked");
          return next;
        },
        { replace: true },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const create = useMutation({
    mutationFn: () => api.createBusGateway({ name: draft.name, informationTypeId: draft.informationTypeId! }),
    onSuccess: (gateway) => {
      clear();
      void queryClient.invalidateQueries({ queryKey: ["bus-gateways"] });
      const base = `/bus-gateways/${gateway.id}`;
      navigate(ctx ? `${withReturn(base, ctx)}&picked=bus-gateway:${gateway.id}` : base);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <div>
      <Link
        to={ctx?.to ?? "/subscriptions?types=bus-gateways"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without creating" : "Integrations"}
      </Link>

      <ReturnBanner />

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
            detourCtx={{ to: here, label: "Creating a new bus gateway" }}
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
