import type { TradeVis } from '../chart/layers/trades';

export type SlotState = 'ok' | 'warn' | 'error' | 'unknown';
export interface Slot {
  state: SlotState;
  value: string;
  detail: string | null;
}
export interface Health {
  slots: Record<string, Slot>;
  now: string;
}

export interface Scope {
  inst: string;
  bartype: string;
  session: string;
  n: number;
  closed: number;
  realTape: boolean;
}

export interface Bar {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface SessionPayload {
  inst: string;
  bartype: string;
  session: string;
  interval: number;
  bars: Bar[];
  trades: TradeVis[];
  tape: {
    synthetic: boolean;
    sidecarMissing: boolean;
    meta: Record<string, unknown> | null;
    path: string;
  };
  barsDerivation: string;
  tickSize: number;
}

/**
 * The sidecar base URL. In `vite dev` /api is proxied; in a packaged shell the
 * page is served BY the sidecar, so a relative path works in both.
 */
const BASE = '';

async function get<T>(path: string): Promise<T> {
  const r = await fetch(`${BASE}${path}`);
  if (!r.ok) throw new Error(`${path} → HTTP ${r.status} ${await r.text()}`);
  return (await r.json()) as T;
}

export const api = {
  health: () => get<Health>('/api/health'),
  scopes: () => get<Scope[]>('/api/scopes'),
  session: (inst: string, bartype: string, session: string, interval = 60) =>
    get<SessionPayload>(
      `/api/session?inst=${encodeURIComponent(inst)}&bartype=${encodeURIComponent(
        bartype,
      )}&session=${encodeURIComponent(session)}&interval=${interval}`,
    ),
};

/** A tiny event log the Terminal pane renders. */
export type LogLevel = 'info' | 'ok' | 'warn' | 'err';
export interface LogLine {
  ts: string;
  level: LogLevel;
  text: string;
}

class LogBus {
  private lines: LogLine[] = [];
  private subs = new Set<(l: LogLine[]) => void>();

  push(level: LogLevel, text: string): void {
    this.lines = [
      ...this.lines.slice(-499),
      { ts: new Date().toISOString().slice(11, 23), level, text },
    ];
    this.subs.forEach((s) => s(this.lines));
  }
  all(): LogLine[] { return this.lines; }
  subscribe(fn: (l: LogLine[]) => void): () => void {
    this.subs.add(fn);
    return () => this.subs.delete(fn);
  }
}
export const log = new LogBus();
