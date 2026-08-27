import { useQuery } from "@tanstack/react-query";
import { getAppConfig } from "../api";

/** Same query key as `useBranding`, so this shares its cache rather than firing a second fetch. */
export function useAppConfig() {
  return useQuery({ queryKey: ["appConfig"], queryFn: getAppConfig });
}

/** Defaults to true while the config is still loading, so pages don't flash a "not configured" state. */
export function useRabbitMqManagementConfigured(): boolean {
  const { data } = useAppConfig();
  return data?.isRabbitMqManagementConfigured ?? true;
}
