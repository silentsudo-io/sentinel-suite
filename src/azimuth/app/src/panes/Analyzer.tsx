import { useEffect, useState } from 'react';
import { api, type SessionPayload } from '../data/api';
import { useSelection } from '../shell/selection';

const n = (v: number | null, d = 1) => (v === null || v === undefined ? '—' : v.toFixed(d));

export function Analyzer() {
  const { scope, selectedTrade, setSelectedTrade } = useSelection();
  const [data, setData] = useState<SessionPayload | null>(null);

  useEffect(() => {
    if (!scope) return;
    let dead = false;
    api
      .session(scope.inst, scope.bartype, scope.session)
      .then((d) => !dead && setData(d))
      .catch(() => !dead && setData(null));
    return () => {
      dead = true;
    };
  }, [scope?.inst, scope?.bartype, scope?.session]);

  const trades = data?.trades ?? [];
  const closed = trades.filter((t) => t.pnlTicks !== null);

  return (
    <div className="pane">
      <div className="pane__bar">
        <span className="microlabel">ANALYZER · TRADES</span>
        <span className="hdr__spacer" />
        <span className="mono" style={{ fontSize: 10.5, color: 'var(--s-mute)' }}>
          {trades.length} rows · {closed.length} closed
        </span>
      </div>
      <div className="pane__body">
        {!scope && <div className="empty">no scope selected</div>}
        {scope && trades.length === 0 && <div className="empty">no trades in this scope</div>}
        {trades.length > 0 && (
          <table className="grid">
            <thead>
              <tr>
                <th>trade_id</th>
                <th>dir</th>
                <th>entry</th>
                <th>exit</th>
                <th>P&amp;L t</th>
                <th>SL°</th>
                <th>TP°</th>
                <th>MFE</th>
                <th>MAE</th>
                <th>end</th>
                <th>signal</th>
              </tr>
            </thead>
            <tbody>
              {trades.slice(0, 500).map((t) => (
                <tr
                  key={t.id}
                  className={selectedTrade === t.id ? 'sel' : ''}
                  onClick={() => setSelectedTrade(selectedTrade === t.id ? null : t.id)}
                >
                  <td style={{ maxWidth: 210, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                    {t.id}
                  </td>
                  <td className={t.dir === 1 ? 'num-up' : 'num-down'}>
                    {t.dir === 1 ? 'L' : 'S'}
                  </td>
                  <td>{n(t.entryPx, 2)}</td>
                  <td className={t.exitPx === null ? 'num-none' : ''}>
                    {t.exitPx === null ? 'open' : n(t.exitPx, 2)}
                  </td>
                  <td
                    className={
                      t.pnlTicks === null
                        ? 'num-none'
                        : t.pnlTicks > 0
                          ? 'num-up'
                          : 'num-down'
                    }
                  >
                    {t.pnlTicks === null ? '—' : `${t.pnlTicks > 0 ? '+' : ''}${n(t.pnlTicks)}`}
                  </td>
                  <td>{n(t.sl, 2)}</td>
                  <td>{n(t.tp, 2)}</td>
                  <td>{n(t.maxFavTicks, 0)}</td>
                  <td>{n(t.maxAdvTicks, 0)}</td>
                  <td className="num-none">{t.endReason ?? '—'}</td>
                  <td className="num-none">{t.signal ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      <div className="legend">
        <span style={{ color: 'var(--s-warn)' }}>
          ° SL/TP derived from barrier_ticks — not a recorded order price
        </span>
        <span style={{ marginLeft: 'auto', color: 'var(--s-mute)' }}>
          STUBBED: equity curve · MAE/MFE brushing · engine-re-running tag filters (spec §4.4)
        </span>
      </div>
    </div>
  );
}
