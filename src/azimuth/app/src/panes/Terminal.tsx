import { useEffect, useRef, useState } from 'react';
import { log, type LogLine } from '../data/api';

const CLS: Record<string, string> = {
  info: '',
  ok: 'term__ok',
  warn: 'term__warn',
  err: 'term__err',
};

/**
 * The Terminal pane.
 *
 * TODAY it streams the app's own event log. It is NOT yet an interactive shell —
 * spec §4.1 wants job output streaming here (backtests, sweeps, gates). The seam
 * is the LogBus; a job runner writes to it over the sidecar and this renders it
 * unchanged.
 */
export function Terminal() {
  const [lines, setLines] = useState<LogLine[]>(log.all());
  const endRef = useRef<HTMLDivElement>(null);
  const [follow, setFollow] = useState(true);

  useEffect(() => log.subscribe(setLines), []);
  useEffect(() => {
    if (follow) endRef.current?.scrollIntoView({ block: 'end' });
  }, [lines, follow]);

  return (
    <div className="pane">
      <div className="pane__bar">
        <span className="microlabel">TERMINAL</span>
        <span className="pill pill--mute">app log</span>
        <span className="hdr__spacer" />
        <span className="mono" style={{ fontSize: 10, color: 'var(--s-mute)' }}>
          job streaming not wired — spec §4.1
        </span>
        <button className={`btn ${follow ? 'btn--on' : ''}`} onClick={() => setFollow((v) => !v)}>
          FOLLOW
        </button>
      </div>
      <div className="pane__body">
        <div className="term">
          {lines.length === 0 && <span style={{ color: 'var(--s-mute)' }}>no output yet</span>}
          {lines.map((l, i) => (
            <div key={i}>
              <span className="term__ts">{l.ts}</span>{' '}
              <span className={CLS[l.level]}>{l.text}</span>
            </div>
          ))}
          <div ref={endRef} />
        </div>
      </div>
    </div>
  );
}
