import { createContext, useContext, useMemo, type MouseEvent, type ReactNode } from "react";
import { Link } from "react-router";
import { api, type ExchangeBreakdown, type ExchangeQuery, type ExchangeRow, type Paged, type RetryTree } from "../../api";
import { useSessionCan } from "../../auth/useSessionCan";
import type { FilterKind } from "./exchangeFilters";

/**
 * Where the Exchanges page reads from, and what it offers. Our team's: every exchange, through the
 * staff API, with every tool. A partner's people's (`partner`): their own, through the portal's API,
 * which limits and strips them on the server; the page then leaves out our tools and the links into
 * our pages. One page for both, so the portal shows exchanges exactly as Bitween does.
 */
export interface ExchangeSource {
  partner: boolean;
  /** Where the list lives, for a link to an exchange or a chain. */
  listPath: string;
  searchExchanges(query: ExchangeQuery, opts?: { count?: boolean }): Promise<Paged<ExchangeRow>>;
  getExchangeBreakdown(query: ExchangeQuery): Promise<ExchangeBreakdown>;
  /** A file's content, by the key its row carries: a storage key for us, a signed link for them. */
  readDocument(key: string): Promise<string>;
  getRetryTree(id: string): Promise<RetryTree>;
  /** May retry, and may choose to reset to the current settings; a partner's always does. */
  canRetry: boolean;
  canChooseReset: boolean;
  retryExchange(id: string, reset: boolean): Promise<{ id: string }>;
  /** Filters not offered. */
  hiddenFilters: ReadonlySet<FilterKind>;
}

const Context = createContext<ExchangeSource | null>(null);

export function useExchangeSource(): ExchangeSource {
  const partner = useContext(Context);
  const canRetry = useSessionCan("exchanges.operate");
  const staff = useMemo<ExchangeSource>(
    () => ({
      partner: false,
      listPath: "/exchanges",
      searchExchanges: (query, opts) => api.searchExchanges(query, opts),
      getExchangeBreakdown: (query) => api.getExchangeBreakdown(query),
      readDocument: (key) => api.getExchangeDocument(key),
      getRetryTree: (id) => api.getRetryTree(id),
      canRetry,
      canChooseReset: true,
      retryExchange: (id, reset) => api.retryExchange(id, { reset }),
      hiddenFilters: new Set(),
    }),
    [canRetry],
  );
  return partner ?? staff;
}

/**
 * A link into one of our own pages, such as a subscription or a partner. A partner's people can't
 * open those, so it is the same text without the link for them.
 */
export function OurPageLink({
  to,
  className = "",
  title,
  onClick,
  children,
}: {
  to: string;
  className?: string;
  title?: string;
  onClick?: (e: MouseEvent) => void;
  children: ReactNode;
}) {
  const { partner } = useExchangeSource();
  if (partner)
    return (
      <span className={className.replace(/\s*hover:link\b/, "")} title={title}>
        {children}
      </span>
    );
  return (
    <Link to={to} className={className} title={title} onClick={onClick}>
      {children}
    </Link>
  );
}

/** The page, and everything it opens, as a partner's people see it. */
export function PartnerExchangeSource({ children }: { children: ReactNode }) {
  const canRetry = useSessionCan("portal-exchanges.operate");
  const canErrors = useSessionCan("portal-exchange-errors.view");
  const value = useMemo<ExchangeSource>(
    () => ({
      partner: true,
      listPath: "/portal",
      searchExchanges: (query, opts) => api.searchExchanges(query, { ...opts, portal: true }),
      getExchangeBreakdown: (query) => api.getExchangeBreakdown(query, { portal: true }),
      readDocument: (url) => api.readExchangeFileLink(url),
      getRetryTree: (id) => api.getRetryTree(id, { portal: true }),
      canRetry,
      canChooseReset: false,
      retryExchange: async (id) => ({ id: await api.retryPortalExchange(id) }),
      // Always their own partner; runs and roll-ups are our wiring; errors only when their role shows them.
      hiddenFilters: new Set<FilterKind>(["partner", "receiveAttempt", "aggregated", ...(canErrors ? [] : ["error" as const])]),
    }),
    [canRetry, canErrors],
  );
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
