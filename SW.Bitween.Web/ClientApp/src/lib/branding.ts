import { useEffect, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { getAppConfig } from "../api";
import { applyColorScale } from "./colorScale";
import { useSettingsDraft } from "./settingsDraft";
import { keys } from "../api/queryKeys";

/**
 * The Brand & theme settings, resolved through the unsaved draft so edits
 * preview live everywhere before they're saved. Image-ish values only count
 * once someone has actually changed them (`customized`); left at the default,
 * each component picks its own shipped asset — which is how the sign-in page
 * gets the light-on-dark logo variant rather than the one-size default.
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
  /** Mobile top-bar mark. Falls back to the sidebar logo when left at the default. */
  headerIconUrl: string | null;
  footer: {
    show: boolean;
    copyrightIcon?: string;
    copyrightText?: string;
    websiteUrl?: string;
    linkedinUrl?: string;
    githubUrl?: string;
  };
}

/** `loginLogo` ⇄ `Theme.LoginLogo` — the draft is keyed by catalog key. */
const catalogKey = (prop: string) => `Theme.${prop[0].toUpperCase()}${prop.slice(1)}`;

export function useBranding(): Branding {
  // Read through the anonymous config endpoint rather than the settings list: the sign-in page
  // has to brand itself with no session, and this way both sides of the door share one path.
  const { data } = useQuery({ queryKey: keys.appConfig, queryFn: getAppConfig });
  const draft = useSettingsDraft();

  return useMemo(() => {
    const theme = data?.theme ?? {};
    const defaults = data?.themeDefaults ?? {};

    const get = (prop: string): string | undefined => {
      const key = catalogKey(prop);
      if (key in draft) return draft[key] ?? defaults[prop] ?? "";
      const value = theme[prop];
      return value == null ? undefined : String(value);
    };
    /** Only meaningful when someone actually changed it away from the default. */
    const customized = (prop: string): string | null => {
      const value = get(prop);
      return value?.trim() && value !== defaults[prop] ? value : null;
    };

    return {
      ready: data !== undefined,
      primaryColor: get("primaryColor"),
      tabTitle: get("tabTitle"),
      companyName: get("companyName"),
      loginBlurb: customized("bitweenText"),
      faviconUrl: customized("tabIcon"),
      loginLogoUrl: customized("loginLogo"),
      sidebarLogoUrl: customized("bitweenLogo"),
      headerIconUrl: customized("bitweenHeaderIcon"),
      footer: {
        show: get("showFooter") !== "false",
        copyrightIcon: get("copyRightsIcon"),
        copyrightText: get("allRightsReserved"),
        websiteUrl: get("websiteLink")?.trim(),
        linkedinUrl: get("linkedinLink")?.trim(),
        githubUrl: get("githubLink")?.trim(),
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
    const existing = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
    const defaultHref = existing?.dataset.defaultHref || existing?.href;
    const href = branding.faviconUrl ?? defaultHref;
    if (!href) return;

    // Replace the element rather than reassigning href. Two reasons: browsers
    // routinely don't re-fetch a favicon when only the href changes, and the
    // markup ships type="image/svg+xml" — left in place that hint misdescribes a
    // .png or .ico override, and the browser drops the icon on the floor. The new
    // element declares no type, so the response's content type decides.
    const link = document.createElement("link");
    link.rel = "icon";
    link.href = href;
    if (defaultHref) link.dataset.defaultHref = defaultHref;
    existing?.remove();
    document.head.appendChild(link);
  }, [branding.faviconUrl]);

  return branding;
}
