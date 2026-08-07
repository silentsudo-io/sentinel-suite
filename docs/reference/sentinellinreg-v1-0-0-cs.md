---
layout: sentinel-ref
title: "SentinelLinReg_v1_0_0.cs"
blurb: "Indicators · 1.0.0 · 230 lines"
---

# SentinelLinReg_v1_0_0.cs

> `bin/Custom/Indicators/SentinelLinReg_v1_0_0.cs`

| | |
|---|---|
| **Family** | Indicators |
| **Version** | 1.0.0 |
| **Size** | 230 lines |
| **Scope** | **public** — ships in `sentinel-suite` |
| **Class** | `SentinelLinReg_v1_0_0` |
| **Namespace** | `NinjaTrader.NinjaScript.Indicators.Sentinel.Smoothers` |
| **Documented by** | _no doc tracks this artifact_ |

> Rendered from the **published** copy in `sentinel-suite/src/`, not the author's private tree — so this page describes the file you actually have.

## What the file says about itself

```text
 Sentinel LinReg — Linear Regression value (Sentinel smoother building block)   |   Version v1.0.0
 File: SentinelLinReg_v1_0_0.cs  |  namespace …Indicators.Sentinel.Smoothers  |  display "Sentinel LinReg"

 ⚠ NO ORDERS · NO STATE SEAM — a read-only SMOOTHER building block, not a Council voter. It draws the
 least-squares regression VALUE (the fitted line evaluated at the current bar), not the slope. It plots a
 smoothed line + a Sentinel glass card; it publishes nothing. A building block the signal tools can consume.

 PROVENANCE / LICENSE: CLEAN-ROOM. Written from the public ordinary-least-squares linear regression
 formula: fit y = a + b·t over the last N inputs (t = 0 oldest … N−1 newest) and return the line value
 at the current bar (t = N−1). slope b = (N·Σt·y − Σt·Σy) / (N·Σt² − (Σt)²); intercept a = (Σy − b·Σt)/N.
 A mathematical method, not copyrightable. No third-party code, variable names, or structure were copied.
 (Sentinel port of the "Au" MA/filter pack; NOT copied. See repo NOTICE.)

 CHANGELOG
   v1.0.0 (2026-07-12) — clean-room least-squares Linear Regression value + Sentinel plumbing (naming law, glass card, label remover).
```

