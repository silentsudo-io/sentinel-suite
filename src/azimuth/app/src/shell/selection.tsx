import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import type { Scope } from '../data/api';

interface SelectionCtx {
  scope: Scope | null;
  setScope: (s: Scope | null) => void;
  /** trade id highlighted from the Analyzer table → drawn cyan in the chart */
  selectedTrade: string | null;
  setSelectedTrade: (id: string | null) => void;
}

const Ctx = createContext<SelectionCtx>({
  scope: null,
  setScope: () => {},
  selectedTrade: null,
  setSelectedTrade: () => {},
});

/**
 * Shared selection across panes. This is deliberately app-level state rather than
 * per-pane: the Explorer, Chart and Analyzer are three views of ONE selection,
 * and a pane that can be popped into its own window still has to agree with the
 * others about what is being looked at.
 */
export function SelectionProvider({ children }: { children: ReactNode }) {
  const [scope, setScope] = useState<Scope | null>(null);
  const [selectedTrade, setSelectedTrade] = useState<string | null>(null);
  const v = useMemo(
    () => ({ scope, setScope, selectedTrade, setSelectedTrade }),
    [scope, selectedTrade],
  );
  return <Ctx.Provider value={v}>{children}</Ctx.Provider>;
}

export const useSelection = () => useContext(Ctx);
