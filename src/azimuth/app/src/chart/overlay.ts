/**
 * THE OVERLAY SEAM.
 *
 * TradingView Lightweight Charts draws price/volume. Everything that makes a chart
 * *Sentinel* — trades, SL/TP runs, seam ribbons, news windows, volume profile,
 * skipped-signal markers, council verdicts — is drawn here, on a canvas the host
 * keeps registered against the chart's coordinate system.
 *
 * This is deliberately a real extension point, not a one-off canvas for the trades
 * layer. A layer gets a frame with time→x and price→y already resolved and draws in
 * pixel space. Layers are independent, ordered, individually toggleable, and may
 * answer hit-tests for hover.
 *
 * Contract for a layer author:
 *   - draw() is called on every invalidation (pan, zoom, resize, data change, hover).
 *     Keep it pure and cheap; do not mutate chart state from inside it.
 *   - timeToX/priceToY return null when the point is not currently resolvable
 *     (off-screen or before data). ALWAYS null-check — a null coordinate silently
 *     becoming 0 is how an overlay ends up drawing a confident line at the top-left
 *     corner of the pane.
 *   - the frame is clipped to the pane rect (price area, excluding the axes) before
 *     draw() is called.
 */

import type { IChartApi, ISeriesApi, Time, UTCTimestamp } from 'lightweight-charts';
import type { SentinelPalette } from '../theme/sentinel';

export interface OverlayFrame {
  ctx: CanvasRenderingContext2D;
  /** pane rect in CSS pixels — the price area, axes excluded */
  pane: { x: number; y: number; w: number; h: number };
  dpr: number;
  pal: SentinelPalette;
  /** null when the time is not currently on screen / not resolvable */
  timeToX(t: Time): number | null;
  /** null when the price is not currently resolvable */
  priceToY(p: number): number | null;
  visible: { from: UTCTimestamp; to: UTCTimestamp } | null;
  hover: { x: number; y: number } | null;
}

export interface OverlayHit {
  layerId: string;
  /** lines of text to show in the hover readout */
  lines: string[];
  /** accent colour for the readout border */
  color?: string;
}

export interface OverlayLayer {
  readonly id: string;
  /** display name for the layer toggle UI */
  readonly label: string;
  enabled: boolean;
  /** lower draws first */
  z: number;
  draw(f: OverlayFrame): void;
  hitTest?(x: number, y: number, f: OverlayFrame): OverlayHit | null;
}

export class OverlayHost {
  private canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private layers: OverlayLayer[] = [];
  private raf = 0;
  private hover: { x: number; y: number } | null = null;
  private ro: ResizeObserver;
  private disposed = false;
  private lastHit: OverlayHit | null = null;
  /** ascending bar times (epoch seconds) — the index behind sub-bar time→x */
  private times: number[] = [];

  /** notified when the hovered overlay item changes (drives the readout chip) */
  public onHit: ((hit: OverlayHit | null) => void) | null = null;

  constructor(
    private container: HTMLElement,
    private chart: IChartApi,
    private series: ISeriesApi<'Candlestick'>,
    private pal: SentinelPalette,
  ) {
    this.canvas = document.createElement('canvas');
    Object.assign(this.canvas.style, {
      position: 'absolute',
      inset: '0',
      pointerEvents: 'none',
      zIndex: '3',
    } as CSSStyleDeclaration);
    container.appendChild(this.canvas);
    this.ctx = this.canvas.getContext('2d')!;

    this.invalidate = this.invalidate.bind(this);
    chart.timeScale().subscribeVisibleLogicalRangeChange(this.invalidate);
    chart.subscribeCrosshairMove((p) => {
      this.hover = p.point ? { x: p.point.x, y: p.point.y } : null;
      this.invalidate();
    });

    this.ro = new ResizeObserver(() => this.invalidate());
    this.ro.observe(container);
    this.invalidate();
  }

  add(layer: OverlayLayer): this {
    this.layers.push(layer);
    this.layers.sort((a, b) => a.z - b.z);
    this.invalidate();
    return this;
  }

  remove(id: string): void {
    this.layers = this.layers.filter((l) => l.id !== id);
    this.invalidate();
  }

  get(id: string): OverlayLayer | undefined {
    return this.layers.find((l) => l.id === id);
  }

  list(): OverlayLayer[] {
    return [...this.layers];
  }

  setPalette(pal: SentinelPalette): void {
    this.pal = pal;
    this.invalidate();
  }

  /**
   * Publish the bar-time index (ascending epoch seconds) that backs time→x.
   * Call this whenever the series data changes.
   */
  setTimeIndex(times: number[]): void {
    this.times = times;
    this.invalidate();
  }

  /**
   * Map an arbitrary timestamp to a FRACTIONAL logical index.
   *
   * ⚠ THIS IS WHY WE DO NOT USE `timeScale().timeToCoordinate()` DIRECTLY.
   *   That call only resolves a time that is exactly a bar's time; anything
   *   between two bars returns null. Trades are intra-bar by nature — an entry
   *   at 13:18:47 on a 1-minute chart matches no bar — so using it directly
   *   silently drops EVERY trade and the overlay renders nothing at all while
   *   looking perfectly healthy.
   *
   * Interpolating a fractional logical index and going through
   * `logicalToCoordinate` fixes both problems at once: sub-bar precision, and
   * points outside the visible range still resolve (needed so a trade that
   * starts off-screen still draws its region and SL/TP runs into view).
   */
  private logicalFor(t: number): number | null {
    const a = this.times;
    if (a.length === 0) return null;
    const last = a.length - 1;
    if (t <= a[0]) {
      const span = a.length > 1 ? a[1] - a[0] : 60;
      return span > 0 ? (t - a[0]) / span : 0;
    }
    if (t >= a[last]) {
      const span = a.length > 1 ? a[last] - a[last - 1] : 60;
      return span > 0 ? last + (t - a[last]) / span : last;
    }
    let lo = 0;
    let hi = last;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (a[mid] <= t) lo = mid;
      else hi = mid;
    }
    const span = a[hi] - a[lo];
    return span > 0 ? lo + (t - a[lo]) / span : lo;
  }

  /** Request a redraw on the next animation frame (coalesced). */
  invalidate(): void {
    if (this.disposed || this.raf) return;
    this.raf = requestAnimationFrame(() => {
      this.raf = 0;
      this.draw();
    });
  }

  private frame(): OverlayFrame | null {
    const rect = this.container.getBoundingClientRect();
    if (rect.width < 2 || rect.height < 2) return null;

    const ts = this.chart.timeScale();
    // Pane = container minus the right price axis and the bottom time axis.
    let axisW = 0;
    let axisH = 0;
    try {
      axisW = this.chart.priceScale('right').width();
      axisH = ts.height();
    } catch {
      /* scales not ready yet — treat as full container */
    }
    const pane = {
      x: 0,
      y: 0,
      w: Math.max(0, rect.width - axisW),
      h: Math.max(0, rect.height - axisH),
    };

    /**
     * Affine anchors for logical→x.
     *
     * ⚠ `logicalToCoordinate()` only honours INTEGER logical indices. Measured on
     *   lightweight-charts 4.2: logical 0 → 485.6 (correct, matches
     *   `timeToCoordinate` for bar 0) but logical 15.3 → 0. A fractional index
     *   silently collapses to zero, which parks every intra-bar object at the
     *   left edge of the pane while still returning a plausible finite number —
     *   so it looks like a rendering bug, not a coordinate bug.
     *
     *   The logical→x mapping is affine (bar spacing is constant), so we sample
     *   it at two INTEGER logicals and interpolate ourselves. Exact, and immune
     *   to the fractional-index behaviour.
     */
    const n = this.times.length;
    let originX: number | null = null;
    let perLogical = 0;
    if (n >= 2) {
      const c0 = ts.logicalToCoordinate(0 as never);
      const cn = ts.logicalToCoordinate((n - 1) as never);
      if (c0 !== null && cn !== null) {
        originX = c0 as number;
        perLogical = ((cn as number) - (c0 as number)) / (n - 1);
      }
    } else if (n === 1) {
      const c0 = ts.logicalToCoordinate(0 as never);
      if (c0 !== null) {
        originX = c0 as number;
        perLogical = 0;
      }
    }

    let visible: { from: UTCTimestamp; to: UTCTimestamp } | null = null;
    try {
      const r = ts.getVisibleRange();
      if (r) visible = { from: r.from as UTCTimestamp, to: r.to as UTCTimestamp };
    } catch {
      /* no data yet */
    }

    return {
      ctx: this.ctx,
      pane,
      dpr: window.devicePixelRatio || 1,
      pal: this.pal,
      timeToX: (t) => {
        const l = this.logicalFor(Number(t));
        if (l === null || originX === null) return null;
        const x = originX + l * perLogical;
        return Number.isFinite(x) ? x : null;
      },
      priceToY: (p) => {
        const c = this.series.priceToCoordinate(p);
        return c === null ? null : (c as number);
      },
      visible,
      hover: this.hover,
    };
  }

  private draw(): void {
    const f = this.frame();
    if (!f) return;

    const rect = this.container.getBoundingClientRect();
    const dpr = f.dpr;
    const w = Math.round(rect.width * dpr);
    const h = Math.round(rect.height * dpr);
    if (this.canvas.width !== w || this.canvas.height !== h) {
      this.canvas.width = w;
      this.canvas.height = h;
      this.canvas.style.width = `${rect.width}px`;
      this.canvas.style.height = `${rect.height}px`;
    }

    const ctx = this.ctx;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, rect.width, rect.height);

    for (const l of this.layers) {
      if (!l.enabled) continue;
      ctx.save();
      ctx.beginPath();
      ctx.rect(f.pane.x, f.pane.y, f.pane.w, f.pane.h);
      ctx.clip();
      try {
        l.draw(f);
      } catch (e) {
        // A throwing layer must not take the chart down with it, but it must also
        // not fail silently — a crashed layer is indistinguishable from an empty one.
        console.error(`[overlay] layer "${l.id}" threw during draw`, e);
      }
      ctx.restore();
    }

    // hover resolution — topmost layer wins
    let hit: OverlayHit | null = null;
    if (f.hover) {
      for (let i = this.layers.length - 1; i >= 0; i--) {
        const l = this.layers[i];
        if (!l.enabled || !l.hitTest) continue;
        try {
          hit = l.hitTest(f.hover.x, f.hover.y, f);
        } catch {
          hit = null;
        }
        if (hit) break;
      }
    }
    const changed = JSON.stringify(hit) !== JSON.stringify(this.lastHit);
    if (changed) {
      this.lastHit = hit;
      this.onHit?.(hit);
    }
  }

  destroy(): void {
    this.disposed = true;
    if (this.raf) cancelAnimationFrame(this.raf);
    this.ro.disconnect();
    this.canvas.remove();
  }
}

// ── shared drawing helpers for layer authors ─────────────────────────────────

export function dashedRun(
  ctx: CanvasRenderingContext2D,
  x0: number,
  x1: number,
  y: number,
  color: string,
  width = 1.25,
  dash: number[] = [5, 4],
): void {
  ctx.save();
  ctx.setLineDash(dash);
  ctx.strokeStyle = color;
  ctx.lineWidth = width;
  ctx.beginPath();
  ctx.moveTo(x0, Math.round(y) + 0.5);
  ctx.lineTo(x1, Math.round(y) + 0.5);
  ctx.stroke();
  ctx.restore();
}

export function roundRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
): void {
  const rr = Math.min(r, w / 2, h / 2);
  ctx.beginPath();
  ctx.moveTo(x + rr, y);
  ctx.arcTo(x + w, y, x + w, y + h, rr);
  ctx.arcTo(x + w, y + h, x, y + h, rr);
  ctx.arcTo(x, y + h, x, y, rr);
  ctx.arcTo(x, y, x + w, y, rr);
  ctx.closePath();
}

/** A floating label chip — the P&L badge, seam tags, etc. */
export function chip(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  text: string,
  fg: string,
  bg: string,
  border: string,
  font: string,
  align: 'left' | 'center' = 'left',
): { w: number; h: number } {
  ctx.save();
  ctx.font = font;
  const padX = 6;
  const padY = 3.5;
  const m = ctx.measureText(text);
  const w = m.width + padX * 2;
  const h = 16;
  const bx = align === 'center' ? x - w / 2 : x;
  const by = y - h / 2;
  ctx.fillStyle = bg;
  roundRect(ctx, bx, by, w, h, 4);
  ctx.fill();
  ctx.strokeStyle = border;
  ctx.lineWidth = 1;
  roundRect(ctx, bx + 0.5, by + 0.5, w - 1, h - 1, 4);
  ctx.stroke();
  ctx.fillStyle = fg;
  ctx.textBaseline = 'middle';
  ctx.textAlign = 'left';
  ctx.fillText(text, bx + padX, y + 0.5);
  ctx.restore();
  return { w, h };
}
