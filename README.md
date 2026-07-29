<div align="center">

# 🛡️ Sentinel Suite

### Make any chart beautiful. Then make it think.

Open-source instruments for **NinjaTrader 8** — a design system that reskins your whole platform,
and a family of sensors that read the tape. Free, forever, and yours to extend.

![License: MPL-2.0](https://img.shields.io/badge/license-MPL--2.0-3FD1E0?style=flat-square)
![Platform: NinjaTrader 8](https://img.shields.io/badge/NinjaTrader-8-25D08B?style=flat-square)
![Themes: 7](https://img.shields.io/badge/themes-7-3FD1E0?style=flat-square)
![Order risk: none](https://img.shields.io/badge/order%20risk-none-25D08B?style=flat-square)

**[Live site »](https://silentsudo-io.github.io/sentinel-suite/)** · [Documentation](docs/) · [The Ladder](#the-ladder)

![The Sentinel suite on a GC futures chart](assets/screenshots/cohesion.png)

</div>

---

## Why Sentinel

Sentinel spans two ideas, kept deliberately separate so you can take either one on its own:

- **Beauty** — a flight-deck design system that makes *any* NinjaTrader chart look considered: glass
  HUD cards, seven cohesive themes, a theme-aware wallpaper. Flip one switch and the whole platform
  re-colors — charts, panels, chrome. Needs zero intelligence.
- **Intelligence** — a family of sensors (trend, momentum, regime, compression, liquidity, order-flow)
  that each stand alone as a clean, readable indicator and publish a state seam other tools can consult.
  Works on a plain chart. Needs zero skin.

One rule runs through all of it: **cyan means *live and watching*** — it's the only accent. Green and
red are reserved for **money and direction**, nothing else. Once you see it, you can't unsee it.

---

## What's inside

Five bundles, each self-contained. Install what you want; ignore the rest.

> ⚠ **Four of them are read-only and cannot place an order. The fifth, the Deck, trades.**
> It is a **testers' preview** — see its row below and read its guide before installing it.

| Bundle | Rung | What you get |
|---|---|---|
| **🎨 [Sentinel Skins](src/skins/)** | 0 · Beauty | 7 platform themes (Dark · Light · Silver · Obsidian · Blueprint · Amber · Neon) · the `SentinelSkin` drawing framework (glass cards + auto-layout) · a theme-aware chart wallpaper |
| **🪟 [Sentinel Binds](src/binds/)** | 0 · Beauty | Window **snapping** (exact, seamless, on release) · named **binds** that drag as one group · named **layouts** you re-apply in a click. No market data, no orders — the safest thing here to read and fork |
| **📡 [Sentinel Sensors](src/sensors/)** | 1 · Intelligence | **8 hero signals** (Trend, ADX, Woodies CCI, VolEnvelope, Compression, Liquidity Walls, God Reversal, WAE) · 13 more (SuperTrend, Regime, Structure, VIDYA, Parabolic SAR…) · **3 bar types** (TBars, TbarsCount, **Flux** — order-flow imbalance) |
| **🎛️ [Sentinel Deck](src/deck/) — PREVIEW (DEV)** | 5 · Execution | **⚠ PLACES REAL ORDERS.** A manual discretionary order deck: full order types, bracket / breakeven / 7 trailing modes, chart-scoped flatten, an account risk card, and a signal reader that can **arm you or fire for you**. **Unfinished and under test** — auto-fire has never been live-validated. **Start on SIM.** **[Tester's guide](src/deck/Docs/SENTINEL_DECK_TESTING_GUIDE.md)** · **[spec](src/deck/Docs/SENTINEL_DECK_SPEC.md)** |
| **〰️ [Sentinel Smoothers](src/smoothers/)** | 1 · Beauty × Data | 23 moving averages & filters, clean-room and rebuilt to the suite's card/color language — EMA · HMA · DEMA · TEMA · VWMA · Zero-Lag family · Ehlers · Gaussian · Butterworth · Super Smoother |

Every tool is naming-law compliant, draws a glass card, and — where it's a signal — publishes a
`…State` seam. See **[docs/](docs/)** for the full reference.

**Installing the Deck also installs the Safety layer** (`src/runtime/SentinelCore.Safety.cs`) — the account-risk decision logic it gates auto-fire through: kill switch, governor, drawdown, session and sizing. That is deliberate ordering, not a bundling accident: **safety ships before autopilot.**

---

## Seven themes. One switch.

| | | |
|:--:|:--:|:--:|
| ![Dark](assets/screenshots/theme-dark.png)<br>**Dark** | ![Light](assets/screenshots/theme-light.png)<br>**Light** | ![Blueprint](assets/screenshots/theme-blueprint.png)<br>**Blueprint** |
| ![Silver](assets/screenshots/theme-silver.png)<br>**Silver** | ![Obsidian](assets/screenshots/theme-obsidian.png)<br>**Obsidian** | ![Amber](assets/screenshots/theme-amber.png)<br>**Amber** |

None are inverted from another — each is designed on its own ground.

---

## Install

No package manager, no build step. NinjaScript source drops straight into the platform. You bring
NinjaTrader 8 — we ship no NinjaTrader binaries.

```bash
# 1 · get the source
git clone https://github.com/silentsudo-io/sentinel-suite

# 2 · copy the bundle(s) you want into NinjaTrader's Custom tree, e.g.
#     Documents\NinjaTrader 8\bin\Custom\
#     (src/runtime/ is shared — copy it once alongside any bundle)

# 3 · in the NinjaScript Editor, press F5 to compile
```

Then right-click a chart → **Skins**, **Indicators**, or **Bar Types** → look for **Sentinel**.
Full steps in the **[docs](docs/)**.

> ### ⚠ If you install more than one bundle, the NEWEST runtime must win
> Every bundle ships a copy of `src/runtime/` (`SentinelCore`, `SentinelSkin`). Copying an **older**
> bundle over a newer one **downgrades the shared runtime**, and because NinjaTrader compiles all of
> `bin\Custom` into **one assembly**, a bundle that needed the newer core then fails to compile and
> takes **your whole tree** down with it — not just Sentinel.
>
> **Rule of thumb: install the oldest bundle first and the newest last**, or just re-copy the newest
> `src/runtime/` after any install. **Every bundle now requires `SentinelCore` v1.41.0 or newer** —
> as of 2026-07-28 they all record swallowed faults through `SentinelCore.Swallow`, which arrived in
> v1.41.0. (This used to be true of `Sentinel Binds` alone.)
>
> Releases here always carry the current runtime. A zip from elsewhere — Discord, an old download —
> may not.

---

## The Ladder

Sentinel is built as a ladder — every rung stands alone *and* unlocks the next. **Rungs 0–1 are
released here.** The rest are built and under active development; the docs map the whole thing so you
can see where it goes.

| | Rung | | | Rung |
|--:|:--|---|--:|:--|
| **0** | Skins & Binds ✅ | | **6** | Prop-Survival Kit |
| **1** | Sensors & Smoothers ✅ | | **7** | The Bridge |
| **2** | Recorder & Log | | **8** | The Copier |
| **3** | Observatory | | **9** | Helm |
| **4** | The Council | | **10** | The ML Lab |
| **5** | Deck & Cockpit | | | |

---

## Documentation

The full canon lives in **[docs/](docs/)** — the design system, the naming law, the field manual, the
dataset dictionary, and a spec for every tool.

## Contributing

Sentinel is a *platform*, not just a suite — the naming law + state-seam protocol let anyone add a
compliant tool. See **[CONTRIBUTING.md](CONTRIBUTING.md)**. Using Claude Code? Run **`/contribute`** —
it turns a change you made in `bin\Custom` into a clean, signed-off pull request.

🔴 **Building your own tool on top of Sentinel? Read [RESERVED_NAMES.md](RESERVED_NAMES.md) first.**
NinjaTrader compiles all of `bin\Custom` into **one assembly**, so two types sharing a name break the
user's *entire* NinjaScript tree — not just Sentinel. That file lists every name this project claims,
**including tools that have not shipped yet**, so you can pick a safe one before you write a line.
Your own prefix and namespace, and we can never collide.

## License & credits

Released under the **[Mozilla Public License 2.0](LICENSE)** — weak, file-level copyleft. The
**SENTINEL** name is a retained trademark; open-sourcing the code doesn't give away the brand.

- Helmet brand mark: *"barbute"* by **Lorc**, [game-icons.net](https://game-icons.net) — CC BY 3.0.
- `LiquidityWalls`: © **TradingIQ**, MPL-2.0.
- `StochasticTripleFilter`: ported from **AlgoTrade_Pro** (MPL-2.0); Gaussian channel © **DonovanWall**.

Full attributions in **[NOTICE](NOTICE)**.

## Disclaimer

Sentinel is for **education and research**. It is **not financial advice**, carries **no warranty**,
and nothing here is a recommendation to trade. Markets carry risk; you are responsible for your own
decisions.

---

<div align="center"><sub>🛡️ Sentinel Suite · built for traders who measure.</sub></div>
