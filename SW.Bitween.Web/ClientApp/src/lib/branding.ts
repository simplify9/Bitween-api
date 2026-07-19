import { useEffect, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api";
import { applyColorScale } from "./colorScale";
import { effectiveValue, useSettingsDraft } from "./settingsDraft";

/**
 * The Brand & theme settings, resolved through the unsaved draft so edits
 * preview live everywhere before they're saved. Image-ish values fall back
 * to the app's shipped assets unless actually customized (`null` = not
 * customized) — the seeded defaults point at legacy backend paths that
 * don't exist in this app.
 */
export interface Branding {
  ready: boolean;
  primaryColor?: string;
  tabTitle?: string;
  companyName?: string;
  /** Override only — null means "use the shipped copy". */
  loginBlurb: string | null;
  /** Overrides only — null means "use the shipped asset". */
  faviconUrl: string | null;
  loginLogoUrl: string | null;
  sidebarLogoUrl: string | null;
  footer: {
    show: boolean;
    copyrightIcon?: string;
    copyrightText?: string;
    websiteUrl?: string;
    linkedinUrl?: string;
    githubUrl?: string;
  };
}

export function useBranding(): Branding {
  const { data } = useQuery({ queryKey: ["settings"], queryFn: () => api.listSettings() });
  const draft = useSettingsDraft();

  return useMemo(() => {
    const get = (key: string): string | undefined => {
      const row = data?.find((r) => r.key === key);
      return row ? effectiveValue(row, draft) : undefined;
    };
    /** Only meaningful when someone actually changed it away from the default. */
    const customized = (key: string): string | null => {
      const row = data?.find((r) => r.key === key);
      if (!row) return null;
      const value = effectiveValue(row, draft);
      return value.trim() && value !== row.defaultValue ? value : null;
    };

    return {
      ready: data !== undefined,
      primaryColor: get("Theme.PrimaryColor"),
      tabTitle: get("Theme.TabTitle"),
      companyName: get("Theme.CompanyName"),
      loginBlurb: customized("Theme.BitweenText"),
      faviconUrl: customized("Theme.TabIcon"),
      loginLogoUrl: customized("Theme.LoginLogo"),
      sidebarLogoUrl: customized("Theme.BitweenLogo"),
      footer: {
        show: get("Theme.ShowFooter") !== "false",
        copyrightIcon: get("Theme.CopyRightsIcon"),
        copyrightText: get("Theme.AllRightsReserved"),
        websiteUrl: get("Theme.WebsiteLink")?.trim(),
        linkedinUrl: get("Theme.LinkedinLink")?.trim(),
        githubUrl: get("Theme.GithubLink")?.trim(),
      },
    };
  }, [data, draft]);
}

/** Side-effect half: keeps the accent color, tab title and favicon in sync. */
export function useApplyBranding(): Branding {
  const branding = useBranding();

  useEffect(() => {
    if (branding.primaryColor) applyColorScale(branding.primaryColor);
  }, [branding.primaryColor]);

  useEffect(() => {
    if (branding.tabTitle) document.title = branding.tabTitle;
  }, [branding.tabTitle]);

  useEffect(() => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    if (!link) return;
    if (!link.dataset.defaultHref) link.dataset.defaultHref = link.href;
    link.href = branding.faviconUrl ?? link.dataset.defaultHref;
  }, [branding.faviconUrl]);

  return branding;
}
