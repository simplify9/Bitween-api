import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useQuery } from "@tanstack/react-query";
import { api, type IntegrationType } from "../../api";
import { Button, FormError } from "../ui/basics";
import { Field, TextInput } from "../ui/forms";
import { Dialog } from "../ui/overlays";
import { CodeBadge } from "../ui/Panel";
import { AdapterConfig, useAdapterCatalog } from "./AdapterConfig";
import { InfoTypePicker } from "./pickers";
import { adapterIncomplete } from "../../pages/integrations/studio/faces";

/**
 * A new gateway-backed integration, asked down to what it cannot run without: a
 * name, the information type it carries, and somewhere to deliver.
 *
 * Deliberately not the whole pipeline. Transformation, validation and response are
 * nodes on the integration's own studio the moment it exists, and reproducing the
 * rail inside a modal would be a worse copy of a surface that already works. The
 * dialog closes on create and hands the id back, so the picker that opened it
 * selects the new integration and you carry on.
 */
export function IntegrationDialog({
  type,
  informationTypeId,
  onClose,
  onCreated,
}: {
  type: Extract<IntegrationType, "GatewayApiCall" | "BusGateway">;
  /** Fixed by the caller (a bus gateway's own type); otherwise it is asked for. */
  informationTypeId?: number;
  onClose: () => void;
  onCreated: (id: number) => void;
}) {
  const queryClient = useQueryClient();
  const handlers = useAdapterCatalog("handler");
  const infoTypes = useQuery({
    queryKey: ["information-types"],
    queryFn: () => api.listInformationTypes(),
    staleTime: Infinity,
  });

  const [name, setName] = useState("");
  const [pickedTypeId, setPickedTypeId] = useState<number | null>(informationTypeId ?? null);
  const [handlerId, setHandlerId] = useState<string | null>(null);
  const [handlerProperties, setHandlerProperties] = useState<Record<string, string>>({});

  const fixedType = informationTypeId ? infoTypes.data?.find((t) => t.id === informationTypeId) : undefined;

  const create = useMutation({
    mutationFn: () =>
      api.createIntegration({
        type,
        name: name.trim(),
        informationTypeId: pickedTypeId!,
        handlerId,
        handlerProperties,
        // Safe to enable: neither type ever runs on its own — a GatewayApiCall waits
        // for an attachment, a BusGateway for a route, and that is what you are in
        // the middle of making.
        enabled: true,
      }),
    onSuccess: (created) => {
      void queryClient.invalidateQueries({ queryKey: ["integrations"] });
      void queryClient.invalidateQueries({ queryKey: ["integration-rows"] });
      void queryClient.invalidateQueries({ queryKey: ["integration-rows-search"] });
      onCreated(created.id);
      onClose();
    },
  });

  const missing = [
    name.trim().length < 2 && "a name",
    pickedTypeId === null && "an information type",
    handlerId === null && "a delivery",
    adapterIncomplete(handlers, handlerId, handlerProperties) && "its required delivery fields",
  ].filter((m): m is string => typeof m === "string");

  return (
    <Dialog title="New integration" onClose={onClose} wide>
      <div className="space-y-4">
        <p className="text-[13px] text-ink-500">
          {type === "GatewayApiCall"
            ? "Runs when a partner calls an API gateway."
            : "Runs when a bus gateway route matches a message."}{" "}
          Transformation and response are configured on its own page once it exists.
        </p>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Name" htmlFor="nid-name">
            <TextInput
              id="nid-name"
              value={name}
              autoFocus
              placeholder="e.g. Coral orders to SAP"
              onChange={(e) => setName(e.target.value)}
            />
          </Field>
          {informationTypeId !== undefined ? (
            <Field label="Carries" hint="Fixed by the gateway this will run for.">
              <p className="flex h-9.5 items-center text-sm text-ink-600">
                {fixedType ? <CodeBadge code={fixedType.code} name={fixedType.name} /> : "…"}
              </p>
            </Field>
          ) : (
            <Field label="Carries" htmlFor="nid-type" hint="The information type this integration processes.">
              <InfoTypePicker id="nid-type" value={pickedTypeId} onChange={setPickedTypeId} />
            </Field>
          )}
        </div>

        <Field label="Delivery" hint="Where the payload ends up.">
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
        <div className="flex items-center justify-end gap-3 border-t border-ink-100 pt-4">
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
