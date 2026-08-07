import { useEffect, useMemo, useRef, useState } from 'react';
import {
  createChart,
  CrosshairMode,
  type IChartApi,
  type ISeriesApi,
  type UTCTimestamp,
} from 'lightweight-charts';
import { OverlayHost, type OverlayHit } from '../chart/overlay';
import { TradesLayer } from '../chart/layers/trades';
import { DARK, FONT_MONO, alpha } from '../theme/sentinel';
import { api, log, type SessionPayload } from '../data/api';
import { useSelection } from '../shell/selection';

export function ChartPane() {
  const { scope, selectedTrade } = useSelection();
  const hostRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const volRef = useRef<ISeriesApi<'Histogram'> | null>(null);
  const overlayRef = useRef<OverlayHost | null>(null);
  const tradesRef = useRef<TradesLayer>(new TradesLayer());
  /** false once the user pans/zooms — stops resize from stealing their viewport */
  const autoFitRef = useRef(true);

  const [hit, setHit] = useState<OverlayHit | null>(null);
  const [data, setData] = useState<SessionPayload | null>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [showTrades, setShowTrades] = useState(true);
  const [interval, setIntervalSec] = useState(60);

  const pal = DARK;

  // ── build the chart once ───────────────────────────────────────────────────
  useEffect(() => {
    if (!hostRef.current) return;
    const chart = createChart(hostRef.current, {
      layout: {
        background: { color: pal.void },
        textColor: pal.mute,
        fontFamily: FONT_MONO,
        fontSize: 10,
      },
      grid: {
        vertLines: { color: alpha(pal.faint, 0.32) },
        horzLines: { color: alpha(pal.faint, 0.32) },
      },
      rightPriceScale: { borderColor: pal.line, scaleMargins: { top: 0.08, bottom: 0.26 } },
      timeScale: { borderColor: pal.line, timeVisible: true, secondsVisible: false },
      crosshair: {
        mode: CrosshairMode.Normal,
        vertLine: { color: alpha(pal.accent, 0.55), width: 1, style: 3, labelBackgroundColor: pal.dim },
        horzLine: { color: alpha(pal.accent, 0.55), width: 1, style: 3, labelBackgroundColor: pal.dim },
      },
      autoSize: true,
    });

    // Candles: green/red is MONEY + DIRECTION — correct for an up/down bar.
    const candles = chart.addCandlestickSeries({
      upColor: pal.up,
      downColor: pal.down,
      borderUpColor: pal.up,
      borderDownColor: pal.down,
      wickUpColor: alpha(pal.up, 0.75),
      wickDownColor: alpha(pal.down, 0.75),
      priceLineVisible: false,
      lastValueVisible: true,
    });

    // Direction-coloured volume on its own overlay scale, pinned to the bottom.
    const vol = chart.addHistogramSeries({
      priceFormat: { type: 'volume' },
      priceScaleId: '',
      priceLineVisible: false,
      lastValueVisible: false,
    });
    vol.priceScale().applyOptions({ scaleMargins: { top: 0.8, bottom: 0 } });

    const overlay = new OverlayHost(hostRef.current, chart, candles, pal);
    overlay.add(tradesRef.current);
    overlay.onHit = setHit;

    // Keep the bars filling the pane while the user has not taken control.
    // Lightweight Charts preserves BAR SPACING across a resize, so widening the
    // pane (docking a tab, maximising the window, dragging a splitter) leaves a
    // dead gutter on the left instead of re-spreading the session. Re-fit on
    // resize — but stop the moment the user pans or zooms, or we would yank the
    // viewport back from under them on every subsequent layout change.
    const ro = new ResizeObserver(() => {
      if (autoFitRef.current) chart.timeScale().fitContent();
      overlay.invalidate();
    });
    ro.observe(hostRef.current);

    const release = () => {
      autoFitRef.current = false;
    };
    const el = hostRef.current;
    el.addEventListener('wheel', release, { passive: true });
    el.addEventListener('mousedown', release);

    chartRef.current = chart;
    candleRef.current = candles;
    volRef.current = vol;
    overlayRef.current = overlay;

    return () => {
      ro.disconnect();
      el.removeEventListener('wheel', release);
      el.removeEventListener('mousedown', release);
      overlay.destroy();
      chart.remove();
      chartRef.current = null;
      overlayRef.current = null;
    };
  }, []);

  // ── load the selected scope ────────────────────────────────────────────────
  useEffect(() => {
    if (!scope) return;
    let cancelled = false;
    setBusy(true);
    setErr(null);
    log.push('info', `loading ${scope.inst} ${scope.bartype} ${scope.session} @ ${interval}s…`);
    api
      .session(scope.inst, scope.bartype, scope.session, interval)
      .then((d) => {
        if (cancelled) return;
        setData(d);
        log.push(
          d.tape.synthetic ? 'warn' : 'ok',
          `${d.bars.length} bars · ${d.trades.length} trades · tape=${
            d.tape.synthetic ? 'SYNTHETIC' : 'REAL'
          }`,
        );
      })
      .catch((e) => {
        if (cancelled) return;
        setErr(String(e.message ?? e));
        log.push('err', `session load failed: ${e.message ?? e}`);
      })
      .finally(() => !cancelled && setBusy(false));
    return () => {
      cancelled = true;
    };
  }, [scope?.inst, scope?.bartype, scope?.session, interval]);

  // ── push data into the chart ───────────────────────────────────────────────
  useEffect(() => {
    const candles = candleRef.current;
    const vol = volRef.current;
    const chart = chartRef.current;
    if (!candles || !vol || !chart || !data) return;

    candles.setData(
      data.bars.map((b) => ({
        time: b.time as UTCTimestamp,
        open: b.open,
        high: b.high,
        low: b.low,
        close: b.close,
      })),
    );
    // direction-coloured: up bar → green, down bar → red (money + direction)
    vol.setData(
      data.bars.map((b) => ({
        time: b.time as UTCTimestamp,
        value: b.volume,
        color: alpha(b.close >= b.open ? pal.up : pal.down, 0.45),
      })),
    );

    tradesRef.current.setTrades(data.trades);
    // The overlay needs the bar-time index to place intra-bar timestamps.
    overlayRef.current?.setTimeIndex(data.bars.map((b) => b.time));

    // Fit twice, deliberately. FlexLayout sizes its tabsets AFTER the pane first
    // mounts, so a single fitContent() here fits the bars to a container that is
    // still narrow; the chart then keeps that bar spacing when autoSize widens
    // it, leaving a wide empty gutter on the left. Re-fitting on the next frame
    // (and once more after layout settles) fits to the real width.
    // A new scope is a new viewport — re-arm auto-fit.
    autoFitRef.current = true;
    const fit = () => chart.timeScale().fitContent();
    fit();
    requestAnimationFrame(fit);
    const t = setTimeout(() => {
      fit();
      overlayRef.current?.invalidate();
    }, 150);
    overlayRef.current?.invalidate();
    return () => clearTimeout(t);
  }, [data]);

  useEffect(() => {
    tradesRef.current.enabled = showTrades;
    overlayRef.current?.invalidate();
  }, [showTrades]);

  // Analyzer row click → cyan emphasis in the chart. Cyan because the selected
  // trade is the one being WATCHED; its money colour still owns the P&L chip.
  useEffect(() => {
    const l = tradesRef.current;
    l.selected.clear();
    if (selectedTrade) l.selected.add(selectedTrade);
    overlayRef.current?.invalidate();
  }, [selectedTrade]);

  const stats = useMemo(() => {
    if (!data) return null;
    const closed = data.trades.filter((t) => t.pnlTicks !== null);
    const wins = closed.filter((t) => (t.pnlTicks ?? 0) > 0).length;
    const net = closed.reduce((a, t) => a + (t.pnlTicks ?? 0), 0);
    return { closed: closed.length, open: data.trades.length - closed.length, wins, net };
  }, [data]);

  return (
    <div className="pane">
      <div className="pane__bar">
        <span className="microlabel">CHART</span>
        {scope ? (
          <span className="mono" style={{ fontSize: 11, color: 'var(--s-ink)' }}>
            {scope.inst} · {scope.bartype} · {scope.session}
          </span>
        ) : (
          <span style={{ color: 'var(--s-mute)', fontSize: 11 }}>no scope selected</span>
        )}

        {data && (
          <span className={`pill ${data.tape.synthetic ? 'pill--warn' : 'pill--accent'}`}>
            {data.tape.synthetic ? 'SYNTHETIC TAPE' : 'REAL TAPE'}
          </span>
        )}
        {data?.tape.sidecarMissing && <span className="pill pill--warn">NO SIDECAR</span>}
        {busy && <span className="pill pill--accent">LOADING…</span>}

        <span className="hdr__spacer" />

        {stats && (
          <span className="mono" style={{ fontSize: 10.5, color: 'var(--s-mute)' }}>
            {stats.closed} closed · {stats.open} open ·{' '}
            <span className={stats.net > 0 ? 'num-up' : stats.net < 0 ? 'num-down' : 'num-none'}>
              {stats.net > 0 ? '+' : ''}
              {stats.net.toFixed(1)}t
            </span>
          </span>
        )}

        <select
          className="btn"
          value={interval}
          onChange={(e) => setIntervalSec(Number(e.target.value))}
          title="Time-bar interval derived from tape"
        >
          <option value={15}>15s</option>
          <option value={60}>1m</option>
          <option value={300}>5m</option>
          <option value={900}>15m</option>
        </select>
        <button
          className={`btn ${showTrades ? 'btn--on' : ''}`}
          onClick={() => setShowTrades((v) => !v)}
        >
          TRADES
        </button>
      </div>

      <div className="pane__body">
        <div ref={hostRef} className="chart-host" />
        {hit && (
          <div className="chart-readout" style={{ borderLeftColor: hit.color ?? pal.accent }}>
            {hit.lines.map((l, i) => (
              <div key={i} style={{ color: i === 0 ? 'var(--s-ink)' : undefined }}>
                {l}
              </div>
            ))}
          </div>
        )}
        {err && (
          <div className="chart-readout" style={{ borderLeftColor: pal.down, top: 8, left: 8 }}>
            <div style={{ color: pal.down }}>LOAD FAILED</div>
            <div>{err}</div>
          </div>
        )}
        {!scope && !err && (
          <div className="empty">
            <div className="microlabel">no scope selected</div>
            <div>Pick an instrument · bartype · session in the Explorer.</div>
          </div>
        )}
      </div>

      <div className="legend">
        <span className="legend__item">
          <span className="legend__box" style={{ background: alpha(pal.up, 0.3), border: `1px solid ${pal.up}` }} />
          winning trade region
        </span>
        <span className="legend__item">
          <span className="legend__box" style={{ background: alpha(pal.down, 0.3), border: `1px solid ${pal.down}` }} />
          losing trade region
        </span>
        <span className="legend__item">
          <span className="legend__box" style={{ background: alpha(pal.accent, 0.3), border: `1px solid ${pal.accent}` }} />
          open / window-expired
        </span>
        <span className="legend__item">
          <span className="legend__swatch" style={{ borderTopColor: pal.down, borderTopStyle: 'dashed' }} />
          SL run
        </span>
        <span className="legend__item">
          <span className="legend__swatch" style={{ borderTopColor: pal.up, borderTopStyle: 'dashed' }} />
          TP run
        </span>
        <span style={{ color: 'var(--s-warn)' }}>
          ° SL/TP are DERIVED from barrier_ticks — the corpus records no order price
        </span>
        {data && <span style={{ marginLeft: 'auto' }}>{data.barsDerivation}</span>}
      </div>
    </div>
  );
}
