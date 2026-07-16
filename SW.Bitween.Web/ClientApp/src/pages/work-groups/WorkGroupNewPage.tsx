import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import { api } from "../../api";
import { Button, FormError } from "../../components/ui/basics";
import { Field, TextInput } from "../../components/ui/forms";
import { ReturnBanner } from "../../components/ui/ReturnBanner";
import { useReturnContext, withReturn } from "../../lib/returnTo";
import { suggestSlug } from "../../lib/identifiers";

export function WorkGroupNewPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const ctx = useReturnContext();
  const [name, setName] = useState("");
  const [busMessageName, setBusMessageName] = useState("");
  const [busNameTouched, setBusNameTouched] = useState(false);
  const [prefetch, setPrefetch] = useState(10);
  const [priority, setPriority] = useState(5);

  const create = useMutation({
    mutationFn: () => api.createWorkGroup({ name, busMessageName, prefetch, priority }),
    onSuccess: (group) => {
      void queryClient.invalidateQueries({ queryKey: ["work-groups"] });
      const base = `/work-groups/${group.id}`;
      navigate(ctx ? `${withReturn(base, ctx)}&picked=workgroup:${group.id}` : base);
    },
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    create.mutate();
  };

  return (
    <div>
      <Link
        to={ctx?.to ?? "/work-groups"}
        className="mb-4 inline-flex items-center gap-1 text-[13px] font-medium text-ink-500 hover:text-ink-800"
      >
        <ArrowLeft className="size-3.5" /> {ctx ? "Back without creating" : "Work groups"}
      </Link>

      <ReturnBanner />

      <h1 className="text-[22px] font-semibold tracking-tight text-ink-900">New work group</h1>
      <p className="mt-1 text-sm text-ink-500">
        Gives its own queue, priority and prefetch to whatever integrations you assign to it.
      </p>

      <form onSubmit={submit} className="mt-6 max-w-lg space-y-4 rounded-xl border border-ink-200 bg-white p-5">
        <Field label="Name" htmlFor="nwg-name">
          <TextInput
            id="nwg-name"
            required
            autoFocus
            value={name}
            onChange={(e) => {
              setName(e.target.value);
              if (!busNameTouched) setBusMessageName(suggestSlug(e.target.value));
            }}
            placeholder="e.g. Priority lane"
          />
        </Field>
        <Field
          label="Bus message name"
          htmlFor="nwg-busname"
          hint="Combined with the group's id to form its queue name."
        >
          <TextInput
            id="nwg-busname"
            required
            value={busMessageName}
            className="font-mono"
            onChange={(e) => {
              setBusNameTouched(true);
              setBusMessageName(e.target.value.toLowerCase());
            }}
            placeholder="priority-lane"
          />
        </Field>
        <div className="grid grid-cols-2 gap-4">
          <Field label="Prefetch" htmlFor="nwg-prefetch" hint="Messages pulled per consumer at once.">
            <TextInput
              id="nwg-prefetch"
              type="number"
              min={1}
              value={prefetch}
              onChange={(e) => setPrefetch(Math.max(1, Number(e.target.value)))}
            />
          </Field>
          <Field label="Priority" htmlFor="nwg-priority" hint="Higher runs before lower.">
            <TextInput
              id="nwg-priority"
              type="number"
              min={0}
              value={priority}
              onChange={(e) => setPriority(Math.max(0, Number(e.target.value)))}
            />
          </Field>
        </div>
        <FormError>{create.error?.message}</FormError>
        <div className="flex justify-end">
          <Button type="submit" variant="primary" busy={create.isPending}>
            Create work group
          </Button>
        </div>
      </form>
    </div>
  );
}
