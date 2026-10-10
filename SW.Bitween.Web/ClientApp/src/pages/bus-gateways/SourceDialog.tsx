import { useState } from "react";
import { Link } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiRequestError, type BusGatewayDetail } from "../../api";
import { Button, FormError, InlineNotice } from "../../components/ui/basics";
import { Field, Select, TextInput } from "../../components/ui/forms";
import { Dialog } from "../../components/ui/overlays";
import { keys } from "../../api/queryKeys";
import { ConnectionBadge } from "../data-sources/ConnectionBadge";
import { providerOf, useDataSourceProviders } from "../data-sources/providers";


/**
 * Where this gateway's messages come from.
 *
 * A dialog rather than a node on the canvas: the canvas draws what happens to a message once it
 * has arrived, and it draws that per route. Where the messages come from is a property of the
 * gateway, so putting it on every route's diagram would say otherwise — the same reason the
 * information type was moved out of the canvas and into the toolbar.
 */
export function SourceDialog({
  gateway,
  onClose,
}: {
  gateway: BusGatewayDetail;
  onClose: () => void;
}) {
  const queryClient = useQueryClient();

  const [dataSourceId, setDataSourceId] = useState<number | null>(gateway.dataSourceId);
  const [endpoint, setEndpoint] = useState(gateway.endpoint ?? "");
  const [error, setError] = useState<string | null>(null);

  const providers = useDataSourceProviders();
  const sources = useQuery({
    queryKey: keys.dataSources.list,
    queryFn: () => api.listDataSources(),
  });

  // Brokers only. A data source can also be a database or an object store held open by a resident
  // adapter, and none of those has a queue for a gateway to read — the API refuses them too, so
  // this is the same rule stated where the operator can see it rather than a second one.
  const available = (sources.data ?? []).filter(
    (s) => (s.kind ?? "Broker").toLowerCase() === "broker",
  );
  const selected = available.find((s) => s.id === dataSourceId);

  const save = useMutation({
    mutationFn: () =>
      api.updateBusGateway(gateway.id, {
        name: gateway.name,
        inactive: gateway.inactive,
        dataSourceId,
        // The endpoint goes with the source. Leaving one behind would show an internal gateway
        // claiming to read a queue, which is the sort of thing that survives unnoticed for a year.
        endpoint: dataSourceId == null ? null : endpoint.trim(),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: keys.busGateways.all });
      onClose();
    },
    onError: (e) =>
      // The API refuses an external gateway with no endpoint, and one whose endpoint another
      // gateway on the same data source already reads. Both are worth reading, not swallowing.
      setError(e instanceof ApiRequestError ? e.message : "Could not change the source."),
  });

  return (
    <Dialog title="Where these messages come from" onClose={onClose}>
      <div className="flex flex-col gap-4">
        <fieldset className="flex flex-col gap-2">
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="radio"
              name="bus-source"
              className="mt-1 size-4 shrink-0 accent-crimson-600"
              checked={dataSourceId == null}
              onChange={() => setDataSourceId(null)}
            />
            <span>
              <span className="text-sm font-medium text-ink-900">Bitween's internal bus</span>
              <span className="block text-[12px] text-ink-500">
                Messages arrive by this information type's bus message name. Every bus gateway was
                this before data sources existed, and it is still the default.
              </span>
            </span>
          </label>

          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="radio"
              name="bus-source"
              className="mt-1 size-4 shrink-0 accent-crimson-600"
              checked={dataSourceId != null}
              disabled={available.length === 0}
              onChange={() => setDataSourceId(available[0]?.id ?? null)}
            />
            <span>
              <span className="text-sm font-medium text-ink-900">A broker outside Bitween</span>
              <span className="block text-[12px] text-ink-500">
                A resident adapter holds the connection and hands each message over. Nothing is
                acknowledged to the broker until Bitween has persisted it.
              </span>
            </span>
          </label>
        </fieldset>

        {available.length === 0 && (
          <InlineNotice>
            No broker data sources exist yet, so there is nothing to point at.{" "}
            <Link to="/data-sources/new" className="font-medium text-crimson-700 hover:underline">
              Add one first
            </Link>
            .
          </InlineNotice>
        )}

        {dataSourceId != null && (
          <>
            <Field label="Data source" htmlFor="bg-source">
              <Select
                id="bg-source"
                value={String(dataSourceId)}
                onChange={(e) => setDataSourceId(Number(e.target.value))}
                options={available.map((s) => ({
                  value: String(s.id),
                  label: `${s.name} — ${providerOf(providers.data, s.adapterId)?.label ?? s.adapterId}${s.inactive ? " (inactive)" : ""}`,
                }))}
              />
            </Field>

            {selected && (
              <div className="flex items-center gap-2 text-sm">
                <span className="text-ink-500">Connection</span>
                <ConnectionBadge state={selected.lastKnownState} failures={selected.consecutiveFailures} />
                <Link
                  to={`/data-sources/${selected.id}`}
                  className="text-[12px] text-crimson-700 hover:underline"
                >
                  open
                </Link>
              </div>
            )}

            <Field
              label="Endpoint"
              htmlFor="bg-endpoint"
              hint="The queue, topic or SQS URL on that data source this gateway reads. It becomes the adapter's consume list, so a gateway without one would sit connected and never receive anything."
            >
              <TextInput
                id="bg-endpoint"
                value={endpoint}
                placeholder="orders.inbound"
                onChange={(e) => setEndpoint(e.target.value)}
              />
            </Field>
          </>
        )}

        {error && <FormError>{error}</FormError>}

        <div className="flex items-center justify-end gap-2">
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="primary" onClick={() => save.mutate()} disabled={save.isPending}>
            {save.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
