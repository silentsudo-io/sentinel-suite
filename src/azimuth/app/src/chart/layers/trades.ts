/**
 * Trade visualisation layer — the reference-platform rendering, per spec §4.2:
 *
 *   "Trades: entry→exit **shaded region**, SL + TP as dashed runs, P&L label at exit"
 *
 * Concretely, for every trade:
 *   1. a SHADED REGION spanning the trade's life in x and entry→exit in y,
 *      tinted by outcome (green = money made, red = money lost);
 *   2. the SL price as its own DASHED HORIZONTAL RUN spanning entry→exit;
 *   3. the TP price as its own DASHED HORIZONTAL RUN spanning entry→exit;
 *   4. entry and exit markers (direction-shaped);
 *   5. the P&L LABEL floating at the exit.
 *
 * Colour law (design system §0): green/red are MONEY + DIRECTION, so the region,
 * the P&L chip and the SL/TP runs use up/down. Cyan is reserved for LIVE/watching
 * and is used only for the selected/hovered trade and for an OPEN (unexited) trade.
 */

import type { UTCTimestamp } from 'lightweight-charts';
import type { OverlayFrame, OverlayHit, OverlayLayer } from '../overlay';
import { chip, dashedRun } from '../overlay';
import { alpha, FONT_MONO } from '../../theme/sentinel';

export interface TradeVis {
  id: string;
  dir: 1 | -1;
  entryTime: UTCTimestamp;
  entryPx: number;
  /** null ⇒ the trade never closed in the corpus (an open/expired excursion) */
  exitTime: UTCTimestamp | null;
  exitPx: number | null;
  pnlTicks: number | null;
  /** stop price. `slDerived` says whether this was RECORDED or COMPUTED. */
  sl: number | null;
  /** target price */
  tp: number | null;
  /** true when sl/tp were derived from barrier_ticks rather than recorded */
  barrierDerived: boolean;
  endReason: string | null;
  signal: string | null;
  conviction: number | null;
  maxFavTicks: number | null;
  maxAdvTicks: number | null;
}

const HIT_PAD = 6;

export class TradesLayer implements OverlayLayer {
  readonly id = 'trades';
  readonly label = 'Trades';
  enabled = true;
  z = 20;

  /** ids of trades to draw emphasised (cyan = watching) */
  selected = new Set<string>();

  private trades: TradeVis[] = [];
  private boxes: Array<{ t: TradeVis; x0: number; x1: number; yTop: number; yBot: number }> = [];

  setTrades(t: TradeVis[]): void {
    this.trades = t;
  }

  count(): number {
    return this.trades.length;
  }

  draw(f: OverlayFrame): void {
    const { ctx, pal, pane } = f;
    this.boxes = [];

    for (const t of this.trades) {
      const x0 = f.timeToX(t.entryTime);
      // An open trade runs to the right edge of the pane rather than vanishing.
      const xExit = t.exitTime !== null ? f.timeToX(t.exitTime) : null;
      if (x0 === null && xExit === null) continue;

      const isOpen = t.exitTime === null;
      const xa = x0 ?? pane.x;
      const xb = isOpen ? pane.x + pane.w : (xExit ?? pane.x + pane.w);
      if (xb < pane.x - 50 || xa > pane.x + pane.w + 50) continue;

      const yEntry = f.priceToY(t.entryPx);
      const yExit = t.exitPx !== null ? f.priceToY(t.exitPx) : null;
      if (yEntry === null) continue;

      const won = (t.pnlTicks ?? 0) > 0;
      const money = won ? pal.up : pal.down;
      const isSel = this.selected.has(t.id);
      // cyan ONLY for live/watching: an open trade, or the hovered/selected one.
      const emph = isSel || isOpen ? pal.accent : money;

      // ── 1. the shaded region ────────────────────────────────────────────────
      const yTop = yExit === null ? yEntry : Math.min(yEntry, yExit);
      const yBot = yExit === null ? yEntry : Math.max(yEntry, yExit);
      const h = Math.max(1.5, yBot - yTop);
      const w = Math.max(1.5, xb - xa);

      ctx.save();
      ctx.fillStyle = alpha(isOpen ? pal.accent : money, isSel ? 0.28 : 0.16);
      ctx.fillRect(xa, yTop, w, h);
      ctx.strokeStyle = alpha(emph, isSel ? 0.95 : 0.6);
      ctx.lineWidth = isSel ? 1.6 : 1;
      ctx.strokeRect(xa + 0.5, yTop + 0.5, w - 1, Math.max(1, h - 1));
      ctx.restore();

      // the entry→exit path across the region
      if (yExit !== null) {
        ctx.save();
        ctx.strokeStyle = alpha(emph, 0.95);
        ctx.lineWidth = isSel ? 2 : 1.4;
        ctx.beginPath();
        ctx.moveTo(xa, yEntry);
        ctx.lineTo(xb, yExit);
        ctx.stroke();
        ctx.restore();
      }

      // ── 2 + 3. SL and TP as their own dashed runs spanning the trade's life ──
      // Dashed, because these are INTENT (where the order rested), not traded price.
      if (t.sl !== null) {
        const ySl = f.priceToY(t.sl);
        if (ySl !== null) {
          dashedRun(ctx, xa, xb, ySl, alpha(pal.down, 0.85), isSel ? 1.8 : 1.25, [5, 4]);
          this.runTag(ctx, xa, ySl, 'SL', pal.down, pal, t.barrierDerived);
        }
      }
      if (t.tp !== null) {
        const yTp = f.priceToY(t.tp);
        if (yTp !== null) {
          dashedRun(ctx, xa, xb, yTp, alpha(pal.up, 0.85), isSel ? 1.8 : 1.25, [5, 4]);
          this.runTag(ctx, xa, yTp, 'TP', pal.up, pal, t.barrierDerived);
        }
      }

      // ── 4. entry / exit markers ─────────────────────────────────────────────
      this.marker(ctx, xa, yEntry, t.dir, emph, pal);
      if (yExit !== null) this.exitMarker(ctx, xb, yExit, money, pal);

      // ── 5. the P&L label, floating at the exit ──────────────────────────────
      if (t.pnlTicks !== null && yExit !== null) {
        const sign = t.pnlTicks > 0 ? '+' : '';
        const txt = `${sign}${t.pnlTicks.toFixed(1)}t`;
        chip(
          ctx,
          xb + 8,
          yExit - 12,
          txt,
          money,
          alpha(money, 0.14),
          alpha(money, 0.5),
          `600 10px ${FONT_MONO}`,
        );
      } else if (isOpen) {
        chip(
          ctx,
          Math.min(xb - 4, pane.x + pane.w - 4) - 46,
          yEntry - 12,
          t.endReason === 'window' ? 'WINDOW' : 'OPEN',
          pal.accent,
          alpha(pal.accent, 0.14),
          alpha(pal.accent, 0.5),
          `600 10px ${FONT_MONO}`,
        );
      }

      this.boxes.push({ t, x0: xa, x1: xb, yTop, yBot });
    }
  }

  /** small "SL"/"TP" tag at the left end of a run; a ° marks a DERIVED price. */
  private runTag(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    text: string,
    col: string,
    pal: { void: string },
    derived: boolean,
  ): void {
    ctx.save();
    ctx.font = `600 9px ${FONT_MONO}`;
    ctx.fillStyle = alpha(pal.void, 0.75);
    const label = derived ? `${text}°` : text;
    const w = ctx.measureText(label).width + 5;
    ctx.fillRect(x - w - 3, y - 6, w, 12);
    ctx.fillStyle = col;
    ctx.textBaseline = 'middle';
    ctx.fillText(label, x - w - 1, y);
    ctx.restore();
  }

  private marker(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    dir: 1 | -1,
    col: string,
    pal: { void: string },
  ): void {
    const s = 5;
    ctx.save();
    ctx.beginPath();
    if (dir === 1) {
      ctx.moveTo(x, y - s);
      ctx.lineTo(x + s, y + s * 0.8);
      ctx.lineTo(x - s, y + s * 0.8);
    } else {
      ctx.moveTo(x, y + s);
      ctx.lineTo(x + s, y - s * 0.8);
      ctx.lineTo(x - s, y - s * 0.8);
    }
    ctx.closePath();
    ctx.fillStyle = col;
    ctx.fill();
    ctx.strokeStyle = pal.void;
    ctx.lineWidth = 1;
    ctx.stroke();
    ctx.restore();
  }

  private exitMarker(
    ctx: CanvasRenderingContext2D,
    x: number,
    y: number,
    col: string,
    pal: { void: string },
  ): void {
    ctx.save();
    ctx.beginPath();
    ctx.arc(x, y, 3.6, 0, Math.PI * 2);
    ctx.fillStyle = col;
    ctx.fill();
    ctx.strokeStyle = pal.void;
    ctx.lineWidth = 1.2;
    ctx.stroke();
    ctx.restore();
  }

  hitTest(x: number, y: number, f: OverlayFrame): OverlayHit | null {
    // topmost (last drawn) wins
    for (let i = this.boxes.length - 1; i >= 0; i--) {
      const b = this.boxes[i];
      if (
        x >= b.x0 - HIT_PAD &&
        x <= b.x1 + HIT_PAD &&
        y >= b.yTop - HIT_PAD &&
        y <= b.yBot + HIT_PAD
      ) {
        const t = b.t;
        const lines = [
          `${t.dir === 1 ? 'LONG' : 'SHORT'}  ${t.id}`,
          `entry ${t.entryPx.toFixed(2)}${t.exitPx !== null ? `  →  exit ${t.exitPx.toFixed(2)}` : '  →  (open)'}`,
        ];
        if (t.pnlTicks !== null) lines.push(`P&L ${t.pnlTicks > 0 ? '+' : ''}${t.pnlTicks.toFixed(1)} ticks`);
        if (t.sl !== null || t.tp !== null) {
          lines.push(
            `SL ${t.sl?.toFixed(2) ?? '—'}   TP ${t.tp?.toFixed(2) ?? '—'}${t.barrierDerived ? '   (° derived from barrier_ticks)' : ''}`,
          );
        }
        if (t.maxFavTicks !== null || t.maxAdvTicks !== null)
          lines.push(`MFE ${t.maxFavTicks ?? '—'}   MAE ${t.maxAdvTicks ?? '—'}`);
        if (t.endReason) lines.push(`end_reason ${t.endReason}`);
        if (t.signal) lines.push(`signal ${t.signal}`);
        return {
          layerId: this.id,
          lines,
          color: (t.pnlTicks ?? 0) > 0 ? f.pal.up : t.pnlTicks === null ? f.pal.accent : f.pal.down,
        };
      }
    }
    return null;
  }
}
