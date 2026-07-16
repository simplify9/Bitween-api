import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { useReturnContext, withReturn } from "../../lib/returnTo";

export function PartnerNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const [name, setName] = useState("");

  const create = useMutation({
    mutationFn: () => api.createPartner({ name }),
    onSuccess: (partner) => {
      void queryClient.invalidateQueries({ queryKey: ["partners"] });
      const base = `/partners/${partner.id}`;
      navigate(ctx ? `${withReturn(base, ctx)}&picked=partner:${partner.id}` : base);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <div>
      <Link
        to={ctx?.to ?? "/partners"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without creating" : "Partners"}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New partner</h1>
      <p className="mt-1 text-sm text-ink-500">
        Just a name to start — properties and API keys are added on the partner's page.
      </p>

      <form onSubmit={submit} className="mt-6 max-w-lg space-y-4 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Partner name" htmlFor="np-name">
          <TextInput
            id="np-name"
            required
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Meridian Logistics"
          />
        </Field>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end">
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create partner
          </Button>
        </div>
      </form>
    </div>
  );
}
