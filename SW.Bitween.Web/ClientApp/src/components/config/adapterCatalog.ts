import { useQuery } from "@tanstack/react-query";
import { api, type AdapterKind } from "../../api";
import { keys } from "../../api/queryKeys";

export function useAdapterCatalog(kind: AdapterKind) {
  return useQuery({
    queryKey: keys.adapters(kind),
    queryFn: () => api.listAdapters(kind),
  });
}
