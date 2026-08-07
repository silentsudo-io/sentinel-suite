import { useEffect, useMemo, useState } from 'react';
import { api, log, type Scope } from '../data/api';
import { useSelection } from '../shell/selection';

type Filter = 'all' | 'tape' | 'closed';

export function Explorer() {
  const { scope, setScope } = useSelection();
  const [scopes, setScopes] = useState<Scope[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('all');

  useEffect(() => {
    api
      .scopes()
      .then((s) => {
        setScopes(s);
        log.push('ok', `explorer: ${s.length} scopes`);
        // Open on the most useful thing available: prefer a session that has BOTH
        // real tape and closed trades; if none does (which is the case today),
        // prefer closed trades so the trade rendering is visible on first paint.
        const best =
          s.find((x) => x.realTape && x.closed > 0) ??
          s.find((x) => x.closed > 0) ??
          s.find((x) => x.realTape) ??
          s[0];
        if (best) setScope(best);
      })
      .catch((e) => {
        setErr(String(e.message ?? e));
        log.push('err', `explorer: ${e.message ?? e}`);
      });
  }, []);

  const groups = useMemo(() => {
    if (!scopes) return [];
    const f = scopes.filter((s) =>
      filter === 'all' ? true : filter === 'tape' ? s.realTape : s.closed > 0,
    );
    const by = new Map<string, Scope[]>();
    for (const s of f) {
      const k = `${s.inst} · ${s.bartype}`;
      if (!by.has(k)) by.set(k, []);
      by.get(k)!.push(s);
    }
    return [...by.entries()].map(([k, v]) => [k, v.sort((a, b) => a.session.localeCompare(b.session))] as const);
  }, [scopes, filter]);

  const counts = useMemo(() => {
    if (!scopes) return { all: 0, tape: 0, closed: 0 };
    return {
      all: scopes.length,
      tape: scopes.filter((s) => s.realTape).length,
      closed: scopes.filter((s) => s.closed > 0).length,
    };
  }, [scopes]);

  return (
    <div className="pane">
      <div className="pane__bar">
        <span className="microlabel">EXPLORER</span>
        <span className="hdr__spacer" />
        {(['all', 'tape', 'closed'] as Filter[]).map((f) => (
          <button
            key={f}
            className={`btn ${filter === f ? 'btn--on' : ''}`}
            onClick={() => setFilter(f)}
            title={
              f === 'tape'
                ? 'sessions with REAL tape on disk'
                : f === 'closed'
                  ? 'sessions containing at least one CLOSED trade'
                  : 'every session in the corpus'
            }
          >
            {f} {counts[f]}
          </button>
        ))}
      </div>
      <div className="pane__body">
        {err && <div className="empty" style={{ color: 'var(--s-down)' }}>{err}</div>}
        {!scopes && !err && <div className="empty">loading corpus scopes…</div>}
        {scopes && groups.length === 0 && <div className="empty">no scopes match this filter</div>}
        <div className="tree">
          {groups.map(([k, rows]) => (
            <div key={k}>
              <div className="tree__group">{k}</div>
              {rows.map((s) => {
                const sel =
                  scope?.inst === s.inst &&
                  scope?.bartype === s.bartype &&
                  scope?.session === s.session;
                return (
                  <div
                    key={s.session}
                    className={`tree__row ${sel ? 'tree__row--sel' : ''}`}
                    onClick={() => setScope(s)}
                    title={`${s.n} rows · ${s.closed} closed · tape ${s.realTape ? 'REAL' : 'synthetic'}`}
                  >
                    <span className="tree__name">{s.session}</span>
                    {/* cyan = LIVE data present. Not decoration — real tape is the
                        difference between research and a random walk. */}
                    {s.realTape && <span className="pill pill--accent">TAPE</span>}
                    {s.closed > 0 && <span className="pill pill--up">{s.closed} closed</span>}
                    <span className="tree__meta">{s.n}</span>
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
