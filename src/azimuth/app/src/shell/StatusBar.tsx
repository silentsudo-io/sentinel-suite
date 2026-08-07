import { useEffect, useState } from 'react';
import { api, log, type Health, type Slot, type SlotState } from '../data/api';

/**
 * The status bar (spec §4.1: interpreter / lib / db / fleet / tz).
 *
 * ⛔ THE INVARIANT: a slot is NEVER blank.
 *
 * Every slot renders one of ok | warn | error | unknown, and `unknown` is a
 * FIRST-CLASS VISIBLE STATE — hollow amber ring, italic amber text, the literal
 * word "unknown", and a trailing "?". This project has been bitten by a status
 * card that rendered an unknown state as empty, which reads as healthy: a blank
 * slot and a green slot are the same glance. So there is no code path here that
 * produces an empty slot — including the one that matters most, when the health
 * probe ITSELF fails, where every slot degrades to `unknown` rather than
 * silently keeping the last good value.
 */

const ORDER = ['python', 'duckdb', 'db', 'tape', 'tz'] as const;

const UNKNOWN: Slot = { state: 'unknown', value: 'unknown', detail: null };

function SlotView({ k, s }: { k: string; s: Slot }) {
  // Defensive: a slot that arrives without a value still renders as unknown,
  // never as an empty cell.
  const state: SlotState = s?.state ?? 'unknown';
  const value = s?.value && String(s.value).trim() ? s.value : 'unknown';
  const shown = state === 'unknown' ? 'unknown' : value;
  return (
    <div className={`slot slot--${state}`} title={s?.detail ?? `${k}: ${shown}`}>
      <span className="slot__dot" />
      <span className="slot__k">{k}</span>
      <span className="slot__v">{shown}</span>
    </div>
  );
}

export function StatusBar() {
  const [health, setHealth] = useState<Health | null>(null);
  const [probeFailed, setProbeFailed] = useState(false);

  useEffect(() => {
    let dead = false;
    const tick = () =>
      api
        .health()
        .then((h) => {
          if (dead) return;
          setHealth(h);
          setProbeFailed(false);
        })
        .catch((e) => {
          if (dead) return;
          // Do NOT keep showing the last good values — a stale green slot is a lie.
          setProbeFailed(true);
          log.push('err', `health probe failed: ${e.message ?? e}`);
        });
    tick();
    const id = setInterval(tick, 10_000);
    return () => {
      dead = true;
      clearInterval(id);
    };
  }, []);

  return (
    <div className="status">
      {ORDER.map((k) => (
        <SlotView key={k} k={k} s={probeFailed || !health ? UNKNOWN : (health.slots[k] ?? UNKNOWN)} />
      ))}
      <div className="slot" style={{ borderRight: 'none' }}>
        <span className="slot__k">sidecar</span>
        <span className="slot__v" style={{ color: probeFailed ? 'var(--s-down)' : 'var(--s-mute)' }}>
          {probeFailed ? 'UNREACHABLE' : (health?.now?.slice(11, 19) ?? 'unknown')}
        </span>
      </div>
    </div>
  );
}
