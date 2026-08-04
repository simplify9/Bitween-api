import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../../api";
import { Button, FormError } from "../../../components/ui/basics";
import { Field, TextInput } from "../../../components/ui/forms";
import { Dialog } from "../../../components/ui/overlays";
import { AdapterConfig, useAdapterCatalog } from "../../../components/config/AdapterConfig";
import { adapterIncomplete } from "../../integrations/studio/faces";

/*
 * Creating from inside the studio.
 *
 * The route itself is never a dialog — it is a row and a set of nodes, so the
 * diagram stays on screen while you answer its three questions. An integration is
 * a *different* record and has to exist before the route can point at it, so it
 * gets a dialog; what it creates is then configured on the canvas like anything
 * else. Partners use the app-wide `PartnerDialog`.
 */

/**
 * A new integration for this gateway, asked down to the two things it can't run
 * without: a name and somewhere to deliver. Transformation, response and the rest
 * are nodes on the canvas the moment it exists — no reason to ask twice.
 */
export function NewIntegrationDialog({
  informationTypeId,
  informationTypeCode,
  onClose,
  onCreated,
}: {
  informationTypeId: number;
  informationTypeCode: string;
  onClose: () => void;
  onCreated: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const handlers = useAdapterCatalog("handler");
  const [name, setName] = useState("");
  const [handlerId, setHandlerId] = useState<string | null>(null);
  const [handlerProperties, setHandlerProperties] = useState<Record<string, string>>({});

  const create = useMutation({
    mutationFn: () =>
      api.createIntegration({
        type: "BusGateway",
        name: name.trim(),
        informationTypeId,
        handlerId,
        handlerProperties,
        // Safe to enable: a BusGateway integration only ever runs through a route,
        // and this one has none until you save the route that is being built.
        enabled: true,
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ["integrations"] });
      void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
      onCreated(created.id);
      onClose();
    },
  });

  const missing = [
    name.trim().length < 2 && "a name",
    handlerId === null && "a delivery",
    adapterIncomplete(handlers, handlerId, handlerProperties) && "its required delivery fields",
  ].filter((m): m is string => typeof m === "string");

  return (
    <Dialog title="New integration" onClose={onClose} wide>
      <div className="space-y-4">
        <Field label="Name" htmlFor="ni-quick-name" hint={`Carries ${informationTypeCode}, like the gateway.`}>
          <TextInput
            id="ni-quick-name"
            value={name}
            autoFocus
            placeholder="e.g. Coral orders to SAP"
            onChange={(e) => setName(e.target.value)}
          />
        </Field>

        <Field label="Delivery" hint="Where matched messages end up. Everything else is configured on the canvas.">
          <AdapterConfig
            kind="handler"
            adapterId={handlerId}
            properties={handlerProperties}
            onChange={(id, props) => {
              setHandlerId(id);
              setHandlerProperties(props);
            }}
            disabled={false}
            required
          />
        </Field>

        <FormError>{create.error?.message}</FormError>
        <div className="flex items-center justify-end gap-3">
          {missing.length > 0 && (
            <p className="text-[13px] text-ink-500">
              Still needs {missing.slice(0, -1).join(", ")}
              {missing.length > 1 ? " and " : ""}
              {missing.at(-1)}.
            </p>
          )}
          <Button onClick={onClose}>Cancel</Button>
          <Button
            variant="primary"
            busy={create.isPending}
            disabled={missing.length > 0}
            onClick={() => create.mutate()}
          >
            Create integration
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
