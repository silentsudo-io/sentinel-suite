# Sentinel Rail — spec

**Status:** Rail proposed (2026-07-09), not built. **The three `CardLayout` bugs in §1 are FIXED and compiling
(2026-07-09) — including scale-to-fit. Needs F5 + a live look.**
**Companion docs:** [SENTINEL_DESIGN_SYSTEM.md](SENTINEL_DESIGN_SYSTEM.md) §4b (Painter / CardLayout) ·
[SENTINEL_COCKPIT_SPEC.md](SENTINEL_COCKPIT_SPEC.md) · memory `sentinel-backlog` (the original tray seeds).

---

## 1. The problem, restated accurately

> **CORRECTED 2026-07-09.** An earlier draft claimed cards no longer overlap. **Wrong** — a live NQ
> screenshot showed the Deck's risk card **buried under GodReversal**. Two further claims were wrong and are
> fixed below: overflow shows up as *overlap* (not only as a lost card), and the cards **can** be rescaled.
> All three `Place()` bugs were **FIXED the same day**; this section is now the post-mortem.

Cards cover roughly **40% of the price panel**, so the requirement is not *organize* — it is **reclaim**.
A rail full of cards occupies the same column a stack of cards does. **The container is not the feature;
the collapse is the feature.** We had this backwards, and it changes the build order (§8).

**Three defects in `Place()` — all now fixed:**

- **Stack order was registration order** (= render order). A card pruned after ~2s of not rendering was
  re-appended to the **end** of the slot list when it returned, so it migrated down the column. Order must
  be **declared**, not emergent — the same lesson as `Roster.conf` (see memory `declared-roster`).
  *Fixed:* a sticky per-`(panel, type)` ordinal that survives pruning.
- **No overflow handling.** The stack offset `off` grew unbounded.
- **No cross-corner awareness.** A corner knew nothing about the opposite corner on the **same edge**, so a
  long TopRight sensor stack grew straight through the BottomRight-anchored Deck card.

The last two are the same defect wearing two masks: **with an opposite anchor present you get OVERLAP;
without one the card runs past the panel edge and is SILENTLY LOST.**

### 1.1 The chart is over-subscribed — and scale-to-fit is why that's survivable

Measured on the NQ chart: TopRight wants 583px of cards, TopLeft 624px, each column has ~625px of panel,
and the Deck claims 187px of the right edge. **No arrangement fits everything.** Gap compression alone
doesn't rescue it (the right column still runs 150px into the Deck), so something must give.

The insight that avoids throwing cards away: a card **draws at fixed pixel sizes with absolute offsets**, so
the arranger can't just hand it a smaller rect — but Direct2D can scale the *render target*. `Painter.Card()`
now looks up its column's scale and pushes a transform anchored on the corner the card docks to; the card
keeps drawing in its natural coordinates and **text stays vector-crisp**. Every sensor card funnels through
`Painter.Card(slot.X, slot.Y, cw, ch, …)`, so this cost **zero indicator edits**.

Fit runs in order of least harm: **compress the gap → scale to `MinCardScale` → COLLAPSE the tail to chips.**
After a collapse the loop *recomputes* the scale, because the survivors may now fit at a larger size.

**Nothing is ever hidden.** This is Stage 1's collapse, delivered early: an overflowed card keeps a **22px chip**
at its real slot carrying its name and a live dot, so the operator always sees the tool is present and rendering.
`Painter.Card()` draws the chip and then *translates* everything the card draws next off-screen — the card lays out
its contents exactly as always and they simply land nowhere. **No card learns it was collapsed.** Collapse is
self-correcting: give the column room and the card re-expands by itself.

Measured on the live chart, with Eye registered (§7) and its 505×346 grid included:

| Column | Result at `MinCardScale = 0.80` |
|---|---|
| **TopRight** (needs 929px in 430px) | VolEnvelope · CompressionBase · SentinelTrend @ 85% · **GodReversal + Eye → chips** |
| **TopLeft** (needs 824px in 625px) | Clock · Location · Excursion @ 87% · **Participation → chip** · Council full |
| **BottomRight** | Deck full size, pinned |
| *(old behaviour)* | Deck buried under GodReversal; Eye drawn over SentinelTrend; MTF clipped off the edge |

### 1.2 The three protected surfaces

**Pinned cards neither scale nor collapse**, and the suite protects exactly three:

| Surface | Card | Why |
|---|---|---|
| **Decision** | Council | Collapse takes the bottom-most card first, and the Council sits at the bottom of the left column — without pinning, *the brain is the first thing chipped.* |
| **Risk** | Deck | A P&L / governor readout that is unreadable is worse than a missing sensor. |
| **Control** | Bridge | Carries `ARM BRIDGE`, hit-tested in untransformed screen coordinates. |

A cosmetic layout pass must never shrink a control out of reach, hide a P&L, or chip the verdict. Sensors may
shrink; the decision may not. Pinning also makes the hit-testing hazard (§5) *disappear* rather than need solving,
since a pinned card is never under a transform.

**Verified before shipping:** no card tool draws chart-space geometry after its `Card()` call (the only
post-`Card()` painter call anywhere is `_sp.Dot(r.Left+5, r.Top+8, …)`, the card's own status dot), and no
raw `RenderTarget` draw follows a card block — so the armed transform can never displace price-space
drawing. `Begin()` resets the shared render target to identity, making a leaked transform impossible even if
an `OnRender` throws between `Card()` and `End()`.

## 2. Why this is SharpDX-on-chart, and why it never floats

The backlog left an architectural fork open: *SharpDX-on-chart (consistent, float is hard) vs.
WPF-off-the-state-seams (float is free, but a parallel card renderer)?*

**That fork is closed.** The **Cockpit** is already the WPF surface that re-reads every published `…State`
seam, floats, and pins `Topmost`. Building a WPF card tray now would build a second Cockpit with worse
ergonomics.

Therefore the two surfaces get disjoint jobs, and the Rail **never floats**:

| Surface | Lives | Answers |
|---|---|---|
| **Cockpit** | off-chart, floatable, pinnable | *"Is my brain alive, and why isn't it trading?"* |
| **Rail** | on-chart, beside price | *"What is each sensor saying about THIS bar?"* — and gets out of the way |

The thing you would float a rail *for* is the Cockpit. It already exists.

## 3. The lever: one choke point

**19 tools call `SentinelSkin.CardLayout.Place()`.** Exactly one card-drawing tool bypasses it
(`Eye_v1_1_0` — retrofit it, §7).

Every card already asks a central authority *"where do I draw?"* every frame. So the Rail is a
**`CardLayout` feature, not 19 edits** — the same central-seam move that let one `Palette` swap recolor
the entire suite.

### 3.1 Collapse costs nothing at the call sites

```csharp
// collapsed → hand the card a rect OUTSIDE the panel.
// It draws its entire card into oblivion; Direct2D clips it. No card learns what "collapsed" means.
return new SharpDX.RectangleF(panelX - w - 64f, y, w, h);
```

A few wasted draw calls per frame. Zero indicator changes. **This is the whole trick.**

## 4. Ownership and the z-order problem (solved by scoping)

`CardLayout` becomes the **arranger** (declared order, overflow, scroll, collapsed state).
A new opt-in `SentinelRail_v1_0_0` indicator becomes the **owner** (chrome, mouse, persistence).

> **If the Rail indicator is not on the chart, `CardLayout` behaves exactly as it does today.**
> Nothing regresses; nothing is forced; it is one F5.

**The z-order hazard and its dissolution.** NT renders indicators in add-order and `Painter` exposes no
frame counter, so there is no reliable hook for "first `Place()` of the frame paints the rail backdrop."
A backdrop drawn by the Rail indicator lands *over* the cards unless the user added it first — brittle.

**So the Rail draws no backdrop at all.**

- **Expanded** = exactly today's look. No chrome. The cards already *are* a rail.
- **Collapsed** = the cards are off-panel, so the strip the Rail draws has nothing to fight with.
- The only always-visible chrome is the **handle**, which lives in the panel margin.

The z-order problem doesn't get solved; it gets **scoped out of existence.** Chrome only exists where no
card is drawing.

## 5. Interactive cards are a SAFETY constraint, not a preference

The Bridge caches `_armBtn` and hit-tests it in `PreviewMouseLeftButtonDown`. Collapsing the card moves
that rect off-panel, so clicks correctly miss.

**Which means collapsing the Bridge card silently removes the only on-chart ARM / DISARM control for an
automated, order-placing strategy.** The Deck's ARM controls have the same shape.

**Rule: a card that owns a control is `Collapsible = false` by default.** Interactive cards stay pinned
when their column collapses. There is no "clever" alternative worth the risk here — a hidden disarm
button is a trap, and the Rail is cosmetic. Cosmetics never hide safety controls.

A collapsed column therefore renders as: **chip strip, then any pinned interactive cards below it.**
`Place()` must offset pinned cards by the chip-strip height, and must **skip collapsed slots when summing
the stack offset** (§6.2).

## 6. API

### 6.1 `Place()` — additive, all 19 call sites unchanged

```csharp
public static SharpDX.RectangleF Place(
    object key, object panelKey,
    float panelX, float panelY, float panelW, float panelH,
    SentinelCardCorner corner, float w, float h,
    float margin = 12f, float gap = 8f,
    // NEW — optional, so every existing caller compiles untouched:
    string title = null,                    // chip label; null → derived from key.GetType().Name
    bool collapsible = true,                // false for cards that own a control (Deck, Bridge)
    SharpDX.Color4? chipTint = null);       // opt-in: Council tints its chip by bias
```

**Chip labels with zero indicator edits.** The key *is* the indicator instance, so
`key.GetType().Name` → `Council_v1_0_0` → strip the `_vX_Y_Z` suffix → `Council`. The `title` parameter
exists so a tool can say something better later. (Note the on-chart `Name` is **not** usable — the
label-remover blanks it; see design system §7.)

### 6.2 Arranger changes

- **Declared order.** `Rail.conf` holds an ordered list of type names per corner. Unlisted cards sort
  after listed ones, alphabetically — stable across prune/re-add. Kills the migration drift in §1.
- **Overflow.** Clamp the column to the panel; expose `scrollOffset` per (panel, corner). When content
  exceeds the panel, the last visible slot is replaced by an **"+N more"** chip. **A card must never
  silently fall off the panel** — that is the current bug.
- **Collapse.** When `(panel, corner)` is collapsed, `collapsible` slots get the off-panel rect and are
  **excluded from the offset sum**; non-collapsible slots stack below the chip strip.

### 6.3 New surface

```csharp
CardLayout.SetCollapsed(object panelKey, SentinelCardCorner corner, bool collapsed);
CardLayout.IsCollapsed (object panelKey, SentinelCardCorner corner);
CardLayout.Scroll      (object panelKey, SentinelCardCorner corner, float delta);
CardLayout.Snapshot    (object panelKey, SentinelCardCorner corner);  // ordered chips for the Rail to draw
```

`Snapshot` returns, per slot: `Title`, `Collapsible`, `ChipTint`, and **`AgeMs`** (from the existing
`Seen` tick). `AgeMs` is what makes a chip honest: it reports **"this card is rendering"**, which is a
real generic liveness signal, not a guess at the tool's semantics. Bias/conviction stay opt-in via
`chipTint`.

## 7. Chips

A collapsed column becomes a **~22px strip**: brand-hairline, then one chip per card —
`● Council`, `● Trend`, `● MTF` … Each chip is `[freshness dot] [name]`, tinted by `chipTint` when the
tool opted in.

- Dot color: `CAccent` when `AgeMs < 2000` (live/watching — the law), `CMute` when stale.
  ⚠ On **Amber** the accent is gold, not cyan; chips read `CAccent` and inherit that for free.
- **Hover-to-peek** (Stage 2): hovering a chip expands *that one card* over the chart until the pointer
  leaves. This is the feature that makes collapse comfortable rather than blind.
- **Click a chip** → expand the whole column.

**Retrofit `Eye_v1_1_0` onto `CardLayout`** so it participates; it is the only card-drawing bypass.

## 8. Build order

Stage 1 carries most of the value and all of the substrate.

**Stage 1 — collapse + chips + the two `Place()` bugs.**
Declared order · overflow scroll · `SetCollapsed` · off-panel rect · chip strip · edge handle ·
`Collapsible=false` on Deck + Bridge · `Rail.conf` persistence · `SentinelRail_v1_0_0`.
*Delivers: the chart back, without losing "is it live?".*

**Stage 2 — hover-to-peek · scroll input (`e.Handled` only inside the rail column, never fight chart zoom)
· remembered per-chart state.**

**Stage 3 — drag-to-reorder · per-card opt-in UI · sub-panel rails.**

## 9. Scope boundaries

- **Price panel only** in Stage 1. `Place` is keyed by `panelKey` (the `ChartPanel`), so sub-panel cards
  (WoodiesCCI, ADXPro, WAE) already bucket separately; a sub-panel rail needs its own Rail instance. Later.
- **No float. Ever.** That is the Cockpit (§2).
- **No new `SentinelCore` seam.** The Rail is decorative chrome + an arranger; the publish-a-`…State`
  protocol (design system §9.6) does not apply — same exemption as `SentinelWallpaper`.
- **Persistence** via `Sentinel\Rail.conf` + `[Display]`-only properties (never `[NinjaScriptProperty]`),
  so NT's generated-region constructor signature is untouched and saved workspaces keep loading.

## 10. Naming & versioning

Federated naming law: file/class `SentinelRail_v1_0_0.cs`, display `Name = "Sentinel Rail"`, namespace
`NinjaTrader.NinjaScript.Indicators.Sentinel` (indicator → picker sub-folder). Label-remover standard.
`SentinelSkin` gets a changelog entry for the `CardLayout` extension.

## 11. Test plan

Offline (build-time):

1. Rail absent → every card lands **pixel-identical** to today. This is the regression gate.
2. Collapse/expand each corner; cards return to the same slots and the **declared order holds** across an
   indicator toggle (the prune/re-append drift).
3. Overflow: add cards until the column exceeds the panel → **"+N more"** appears, no card is lost.
4. All six themes: chips legible; the dot reads as *live* on Amber (gold) as it does on cyan themes.
5. F5 safety: `Place()`'s new params are optional → 19 call sites compile untouched.

Live (market open):

6. **The safety test.** Collapse both columns with the Bridge armed. **`ARM BRIDGE` must remain visible
   and clickable.** Then disarm from the chart. If the control is ever unreachable, Stage 1 is wrong.
7. Hover-peek does not steal chart-zoom scroll (Stage 2).

## 12. Open questions

- Does a collapsed column keep the **Excursion** card's recording indicator visible? It has no control,
  but it is stateful. Probably `chipTint` is enough.
- Should the chip strip show `+N more` or scroll when *collapsed*? (Chips are small; likely never overflows.)
- Does hover-peek need a delay to avoid flicker when crossing the strip? Almost certainly yes (~250ms).
